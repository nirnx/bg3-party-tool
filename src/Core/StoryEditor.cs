using System.Text.Json.Nodes;
using LSLib.LS.Story;

namespace Bg3PartyTool.Core;

/// <summary>
/// Removes and re-inserts facts in an Osiris StorySave.bin without re-encoding the rest of it.
///
/// LSLib's writer encodes value flags differently from the game (0x0a becomes 0x0b), so its output is never kept. It is
/// only used to locate bytes: writing the story before and after one fact is removed (or added), the diff gives that
/// fact's exact span and the changed fact count. The edit is then applied to the game's own bytes, and the result is
/// verified by parsing it back.
/// </summary>
public static class StoryEditor
{
    public sealed record StoryFact(string Db, string Text, byte[] Raw, JsonArray Columns);

    static Story ReadStory(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return new StoryReader().Read(ms);
    }

    static byte[] WriteStory(Story story)
    {
        var ms = new MemoryStream();
        new StoryWriter().Write(ms, story, true);
        return ms.ToArray();
    }

    static string FactText(Fact f) => string.Join("\t", f.Columns.Select(c => c.ToString()));

    static JsonArray ColumnsJson(Fact f) => new(f.Columns.Select(c => (JsonNode)new JsonObject
    {
        ["TypeId"] = c.TypeId, ["IntValue"] = c.IntValue, ["Int64Value"] = c.Int64Value, ["FloatValue"] = c.FloatValue,
        ["StringValue"] = c.StringValue, ["IsValid"] = c.IsValid, ["OutParam"] = c.OutParam, ["IsAType"] = c.IsAType,
        ["Index"] = c.Index, ["Unused"] = c.Unused, ["Adapted"] = c.Adapted,
    }).ToArray());

    static Fact FactFromJson(JsonArray cols) => new()
    {
        Columns = cols.Select(n => new Value
        {
            TypeId = n!["TypeId"]!.GetValue<uint>(), IntValue = n["IntValue"]!.GetValue<int>(), Int64Value = n["Int64Value"]!.GetValue<long>(),
            FloatValue = n["FloatValue"]!.GetValue<float>(), StringValue = n["StringValue"]?.GetValue<string>()!,
            IsValid = n["IsValid"]!.GetValue<bool>(), OutParam = n["OutParam"]!.GetValue<bool>(), IsAType = n["IsAType"]!.GetValue<bool>(),
            Index = n["Index"]!.GetValue<sbyte>(), Unused = n["Unused"]!.GetValue<bool>(), Adapted = n["Adapted"]!.GetValue<bool>(),
        }).ToList(),
    };

    public static JsonObject ToJson(StoryFact f) => new()
    {
        ["db"] = f.Db, ["text"] = f.Text, ["raw"] = Convert.ToHexStringLower(f.Raw), ["columns"] = f.Columns.DeepClone(),
    };

    public static StoryFact FromJson(JsonNode n) => new(
        n["db"]!.GetValue<string>(), n["text"]!.GetValue<string>(), Convert.FromHexString(n["raw"]!.GetValue<string>()), n["columns"]!.AsArray());

    static Dictionary<string, Database> DatabasesByName(Story story)
    {
        var d = new Dictionary<string, Database>();
        foreach (var db in story.Databases.Values)
            if (db.OwnerNode?.Name is { } name) d.TryAdd(name, db);
        return d;
    }

    /// <summary>Removes facts whose columns contain <paramref name="needle"/> from the named databases.</summary>
    public static (byte[] story, List<StoryFact> removed) RemoveFacts(byte[] original, string needle, IReadOnlySet<string> databases)
    {
        var a = (byte[])original.Clone();
        var story = ReadStory(a);
        var b0 = WriteStory(story);
        if (a.Length != b0.Length) throw new SaveException("LSLib changed the size of the Osiris database on write; aborting");

        var removed = new List<StoryFact>();
        foreach (var (name, db) in DatabasesByName(story))
        {
            if (!databases.Contains(name)) continue;
            for (int i = db.Facts.Count - 1; i >= 0; i--)
            {
                var fact = db.Facts[i];
                if (!FactText(fact).Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
                db.Facts.RemoveAt(i);
                var b1 = WriteStory(story);
                int len = b0.Length - b1.Length;

                // smallest j with b0[j..] == b1[j-len..]; the removed fact is b0[j-len .. j)
                int j = b0.Length;
                while (j - len > 0 && b0[j - 1] == b1[j - 1 - len]) j--;
                int cut = j - len;
                var changed = ChangedBefore(b0, b1, cut, name);

                foreach (var c in changed) a[c] = b1[c];
                // When the fact shares bytes with its neighbour, every cut up to `last` removes the same bytes; export
                // the one that starts where the fact does (its column count), so it can be put back on its own later.
                int last = cut;
                while (last + len < a.Length && a[last] == a[last + len]) last++;
                for (int k = cut; k <= last; k++)
                    if (a[k] == fact.Columns.Count) { cut = k; break; }
                removed.Add(new StoryFact(name, FactText(fact), a[cut..(cut + len)], ColumnsJson(fact)));
                a = [.. a.AsSpan(0, cut), .. a.AsSpan(cut + len)];
                b0 = b1;
            }
        }
        Verify(a, b0);
        return (a, removed);
    }

    /// <summary>
    /// Appends facts to their databases, inserting the game's original bytes from <see cref="RemoveFacts"/>.
    /// Facts that already exist are skipped; so are facts rejected by <paramref name="skip"/> (e.g. a slot taken meanwhile).
    /// </summary>
    public static (byte[] story, List<string> added, List<string> skipped) InsertFacts(byte[] original, IEnumerable<StoryFact> facts, Func<StoryFact, IEnumerable<string>, string?> skip)
    {
        var a = (byte[])original.Clone();
        var story = ReadStory(a);
        var b0 = WriteStory(story);
        if (a.Length != b0.Length) throw new SaveException("LSLib changed the size of the Osiris database on write; aborting");
        var dbs = DatabasesByName(story);

        List<string> added = new(), skipped = new();
        foreach (var f in facts)
        {
            if (!dbs.TryGetValue(f.Db, out var db))
            {
                skipped.Add($"{f.Db}: brak takiej bazy");
                continue;
            }
            var existing = db.Facts.Select(FactText).ToList();
            if (existing.Contains(f.Text))
            {
                skipped.Add($"{f.Db}: już jest");
                continue;
            }
            if (skip(f, existing) is { } reason)
            {
                skipped.Add($"{f.Db}: {reason}");
                continue;
            }

            var fact = FactFromJson(f.Columns);
            db.Facts.Add(fact);
            var b1 = WriteStory(story);
            int len = b1.Length - b0.Length;
            if (len != f.Raw.Length) throw new SaveException($"{f.Db}: fact encodes to {len} B but the export has {f.Raw.Length} B");

            // The new fact sits somewhere in [first, last]: smallest start whose tail still matches, largest whose head
            // does. When it shares bytes with a neighbour every start in between encodes the same under LSLib, but the
            // game's own bytes only fit at the real fact boundary, so try each start until the story parses back.
            int j = b1.Length;
            while (j - len > 0 && b1[j - 1] == b0[j - 1 - len]) j--;
            int first = j - len, last = first;
            while (last < b0.Length && b1[last] == b0[last]) last++;
            var changed = ChangedBefore(b0, b1, first, f.Db);
            foreach (var c in changed) a[c] = b1[c];

            byte[]? spliced = null;
            for (int at = first; at <= last && spliced == null; at++)
            {
                byte[] candidate = [.. a.AsSpan(0, at), .. f.Raw, .. a.AsSpan(at)];
                if (first == last || Reencodes(candidate, b1)) spliced = candidate;
            }
            a = spliced ?? throw new SaveException($"{f.Db}: found no place for the fact in the Osiris database");
            b0 = b1;
            added.Add(f.Db);
        }
        Verify(a, b0);
        return (a, added, skipped);
    }

    /// <summary>Positions before the edit where the two encodings differ: only the database's fact count may.</summary>
    static List<int> ChangedBefore(byte[] b0, byte[] b1, int edit, string db)
    {
        var changed = new List<int>();
        for (int k = 0; k < edit; k++) if (b0[k] != b1[k]) changed.Add(k);
        if (changed.Count == 0 || changed.Count > 4 || edit - changed[0] > 1_000_000)
            throw new SaveException($"unexpected difference while editing {db} ({changed.Count} bytes)");
        return changed;
    }

    static bool Reencodes(byte[] edited, byte[] expectedEncoding)
    {
        try { return WriteStory(ReadStory(edited)).AsSpan().SequenceEqual(expectedEncoding); }
        catch (Exception) { return false; } // a misplaced splice usually doesn't even parse
    }

    static void Verify(byte[] edited, byte[] expectedEncoding)
    {
        if (!WriteStory(ReadStory(edited)).AsSpan().SequenceEqual(expectedEncoding))
            throw new SaveException("Osiris database verification failed");
    }
}
