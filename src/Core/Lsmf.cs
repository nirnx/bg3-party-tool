using System.Buffers.Binary;
using System.Text;

namespace Bg3PartyTool.Core;

/// <summary>
/// Reader/editor for the LSMF ("NewAge") ECS blob stored in a BG3 save's Globals.lsf.
///
/// Layout (Patch 8+, header version 0x08000101), as far as it has been reversed:
/// - 48-byte header: magic "LSMF", u32 version, u64 hash, u64 blockOffset, u64 blockSize,
///   u32 nameBlockSize, u16 componentCount, u16, u64. Every offset in the file is relative to the end of it.
/// - Component table at blockOffset + nameBlockSize, one 48-byte entry per component:
///   u64 nameOffset, u32 nameLen, u32 flags, u64 hash, u32 elementSize, u32 version, u64 elementCount, u64 dataOffset.
/// - Component 0, core.v0.Level, holds [entitiesStart, entitiesEnd) of 16-byte GUIDs and [mapsStart, mapsEnd) of
///   32-byte owner maps: u64 indicesStart, indicesEnd, componentIndex, count. Element k of a component belongs to
///   entity indices[k]. Unused map slots have componentIndex all-ones.
/// - Entity references are u64 offsets into the core.v0.EntityId element array (16-byte GUIDs).
/// - Ranges are [start, end) pairs of u64 offsets; an empty range stores all-ones.
///
/// Edits never move data between components: elements are deleted by shifting the rest of the same array down and
/// shrinking its count, and ranges are compacted in place, so every pointer elsewhere stays valid.
/// </summary>
public sealed class Lsmf
{
    public const int Base = 48;
    public const ulong Empty = ulong.MaxValue;
    public const uint SupportedVersion = 0x08000101;
    const string Level = "core.v0.Level";
    const string EntityIdComponent = "core.v0.EntityId";

    public sealed class Component
    {
        public required int Index;
        public required string Name;
        public required int Size;
        public required int Count;
        public required ulong Offset;
        public required int EntryPos; // absolute position of the table entry
        public List<int>? Owners; // entity indices, null for side tables
        public int MapPos = -1; // absolute position of the owner map entry
    }

    byte[] _data;
    public readonly Dictionary<string, Component> Components = new();
    readonly List<Component> _byIndex = new();
    public readonly List<byte[]> Entities = new();
    readonly Dictionary<Guid, int> _entityIndex = new();

    public Lsmf(byte[] data)
    {
        if (data.Length < Base || Encoding.ASCII.GetString(data, 0, 4) != "LSMF")
            throw new SaveException("NewAge is not an LSMF blob");
        _data = (byte[])data.Clone();
        var version = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(4));
        if (version != SupportedVersion)
            throw new SaveException($"unsupported LSMF version {version:x} (tested with {SupportedVersion:x}); save from another game version?");
        var bodyLen = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(16)) + BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(24));
        if ((ulong)Base + bodyLen > (ulong)_data.Length
            || Hash(_data.AsSpan(Base, (int)bodyLen)) != BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(8)))
            throw new SaveException("NewAge checksum mismatch (different game version?); the game would reject the result");
        var blk = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(16));
        var nameSize = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(32));
        var n = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(36));
        for (int i = 0; i < n; i++)
        {
            int pos = checked(Base + (int)blk + (int)nameSize + i * 48);
            var e = _data.AsSpan(pos, 48);
            var nameOff = BinaryPrimitives.ReadUInt64LittleEndian(e);
            var nameLen = BinaryPrimitives.ReadUInt32LittleEndian(e[8..]);
            var c = new Component
            {
                Index = i,
                Name = Encoding.UTF8.GetString(Read(blk + nameOff, (int)nameLen)),
                Size = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[24..]),
                Count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(e[32..])),
                Offset = BinaryPrimitives.ReadUInt64LittleEndian(e[40..]),
                EntryPos = pos,
            };
            Components[c.Name] = c;
            _byIndex.Add(c);
        }

        var lvl = Comp(Level);
        var (es, ee) = Range(lvl.Offset);
        var (ms, me) = Range(lvl.Offset + 16);
        for (var o = es; o < ee; o += 16)
        {
            var g = Read(o, 16);
            _entityIndex.TryAdd(new Guid(g), Entities.Count);
            Entities.Add(g);
        }
        for (var o = ms; o < me; o += 32)
        {
            var ist = U64(o);
            var ci = U64(o + 16);
            var cnt = (int)U64(o + 24);
            if (ci == Empty) continue;
            var comp = _byIndex[(int)ci];
            comp.Owners = new List<int>(cnt);
            if (ist != Empty)
                for (int k = 0; k < cnt; k++) comp.Owners.Add((int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Base + (int)ist + 4 * k)));
            comp.MapPos = Base + (int)o;
            if (comp.Owners.Count != comp.Count)
                throw new SaveException($"{comp.Name}: owner map has {comp.Owners.Count} entries for {comp.Count} elements");
        }
    }

    /// <summary>The blob with its header hash recomputed; the game refuses to load ("server shutdown") on a mismatch.</summary>
    public byte[] ToArray()
    {
        var data = (byte[])_data.Clone();
        var len = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(16)) + BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(24));
        if ((ulong)Base + len > (ulong)data.Length) throw new SaveException("NewAge: header points outside the blob");
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), Hash(data.AsSpan(Base, (int)len)));
        return data;
    }

    /// <summary>Header hash (reversed from the game's loader): MurmurHash3 x64_128 over the [blockOffset + blockSize)
    /// bytes after the header, both halves seeded with 0xf0015eed5eedf001, returning h1 after finalisation.</summary>
    public static ulong Hash(ReadOnlySpan<byte> d)
    {
        const ulong seed = 0xf0015eed5eedf001, c1 = 0x87c37b91114253d5, c2 = 0x4cf5ad432745937f;
        static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));
        static ulong Fmix(ulong k)
        {
            k ^= k >> 33; k *= 0xff51afd7ed558ccd;
            k ^= k >> 33; k *= 0xc4ceb9fe1a85ec53;
            return k ^ (k >> 33);
        }

        ulong h1 = seed, h2 = seed;
        int blocks = d.Length / 16;
        for (int i = 0; i < blocks; i++)
        {
            ulong k1 = BinaryPrimitives.ReadUInt64LittleEndian(d[(i * 16)..]), k2 = BinaryPrimitives.ReadUInt64LittleEndian(d[(i * 16 + 8)..]);
            k1 *= c1; k1 = Rotl(k1, 31); k1 *= c2; h1 ^= k1;
            h1 = Rotl(h1, 27); h1 += h2; h1 = h1 * 5 + 0x52dce729;
            k2 *= c2; k2 = Rotl(k2, 33); k2 *= c1; h2 ^= k2;
            h2 = Rotl(h2, 31); h2 += h1; h2 = h2 * 5 + 0x38495ab5;
        }
        var tail = d[(blocks * 16)..];
        ulong t1 = 0, t2 = 0;
        for (int i = tail.Length - 1; i >= 8; i--) t2 = (t2 << 8) | tail[i];
        for (int i = Math.Min(tail.Length, 8) - 1; i >= 0; i--) t1 = (t1 << 8) | tail[i];
        if (tail.Length > 8) { t2 *= c2; t2 = Rotl(t2, 33); t2 *= c1; h2 ^= t2; }
        if (tail.Length > 0) { t1 *= c1; t1 = Rotl(t1, 31); t1 *= c2; h1 ^= t1; }
        h1 ^= (ulong)d.Length; h2 ^= (ulong)d.Length;
        h1 += h2; h2 += h1;
        h1 = Fmix(h1); h2 = Fmix(h2);
        return h1 + h2;
    }

    // --- low-level access ---------------------------------------------------------------------------------------

    public byte[] Read(ulong off, int size)
    {
        if (Base + off + (ulong)size > (ulong)_data.Length) throw new SaveException($"read {size}@{off:x} outside the blob");
        return _data.AsSpan(Base + (int)off, size).ToArray();
    }

    public void Write(ulong off, ReadOnlySpan<byte> data) => data.CopyTo(_data.AsSpan(Base + (int)off));
    public ulong U64(ulong off) => BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(Base + (int)off));
    public void SetU64(ulong off, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(Base + (int)off), v);
    public (ulong start, ulong end) Range(ulong off) => (U64(off), U64(off + 8));
    public string String(ulong off, int len) => Encoding.UTF8.GetString(Read(off, len));

    // --- components -----------------------------------------------------------------------------------------------

    public Component Comp(string name) =>
        Components.TryGetValue(name, out var c) ? c : throw new SaveException($"missing component {name}");

    public bool Has(string name) => Components.ContainsKey(name);

    public ulong ElementOffset(Component c, int k) => c.Offset + (ulong)(k * c.Size);

    public IEnumerable<(int? owner, byte[] raw)> Elements(string name)
    {
        if (!Components.TryGetValue(name, out var c)) yield break;
        for (int k = 0; k < c.Count; k++) yield return (c.Owners?[k], Read(ElementOffset(c, k), c.Size));
    }

    public Dictionary<int, byte[]> ByEntity(string name)
    {
        var d = new Dictionary<int, byte[]>();
        foreach (var (o, raw) in Elements(name)) if (o is int e) d[e] = raw;
        return d;
    }

    public int? ElementOf(string name, int entity)
    {
        var c = Comp(name);
        if (c.Owners == null) throw new SaveException($"{name} has no owners");
        var k = c.Owners.IndexOf(entity);
        return k < 0 ? null : k;
    }

    void StoreCount(Component c)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(c.EntryPos + 32), (ulong)c.Count);
        if (c.MapPos >= 0)
        {
            var map = _data.AsSpan(c.MapPos, 32);
            var ist = BinaryPrimitives.ReadUInt64LittleEndian(map);
            if (c.Count > 0)
            {
                for (int k = 0; k < c.Count; k++)
                    BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(Base + (int)ist + 4 * k), (uint)c.Owners![k]);
                BinaryPrimitives.WriteUInt64LittleEndian(map[8..], ist + 4 * (ulong)c.Count);
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(map, Empty);
                BinaryPrimitives.WriteUInt64LittleEndian(map[8..], Empty);
            }
            BinaryPrimitives.WriteUInt64LittleEndian(map[24..], (ulong)c.Count);
        }
        if (c.Count == 0) BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(c.EntryPos + 40), Empty);
    }

    /// <summary>Delete the entity's element from an owned component, keeping the order of the others.</summary>
    public byte[]? RemoveElement(string name, int entity)
    {
        var c = Comp(name);
        if (ElementOf(name, entity) is not int k) return null;
        var old = Read(ElementOffset(c, k), c.Size);
        var tail = Read(ElementOffset(c, k + 1), (c.Count - k - 1) * c.Size);
        Write(ElementOffset(c, k), tail);
        Write(ElementOffset(c, c.Count - 1), new byte[c.Size]);
        c.Owners!.RemoveAt(k);
        c.Count--;
        StoreCount(c);
        return old;
    }

    /// <summary>Hand element k to another entity, then keep elements sorted by owner like the game stores them.</summary>
    public void SetOwner(string name, int k, int entity)
    {
        var c = Comp(name);
        c.Owners![k] = entity;
        var rows = Enumerable.Range(0, c.Count).Select(i => (owner: c.Owners[i], raw: Read(ElementOffset(c, i), c.Size)))
            .OrderBy(r => r.owner).ToList();
        c.Owners = rows.Select(r => r.owner).ToList();
        Write(c.Offset, rows.SelectMany(r => r.raw).ToArray());
        StoreCount(c);
    }

    // --- growing -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Append bytes to the end of the data area (just before the name block and component table, which move up) and
    /// return their offset. Existing data never moves, so every stored pointer stays valid.
    /// </summary>
    public ulong Append(ReadOnlySpan<byte> bytes)
    {
        var blk = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(16));
        int n = (bytes.Length + 15) & ~15;
        int at = Base + (int)blk;
        var nd = new byte[_data.Length + n];
        _data.AsSpan(0, at).CopyTo(nd);
        bytes.CopyTo(nd.AsSpan(at));
        _data.AsSpan(at).CopyTo(nd.AsSpan(at + n));
        BinaryPrimitives.WriteUInt64LittleEndian(nd.AsSpan(16), blk + (ulong)n);
        _data = nd;
        foreach (var c in _byIndex) c.EntryPos += n;
        return blk;
    }

    /// <summary>Give an entity a new element of an owned component (array and owner list are moved to fresh space).</summary>
    public void InsertElement(string name, int entity, byte[] element)
    {
        var c = Comp(name);
        if (c.Owners == null || c.MapPos < 0) throw new SaveException($"{name} has no owner map");
        if (element.Length != c.Size) throw new SaveException($"{name}: element is {element.Length} B, expected {c.Size}");
        if (c.Owners.Contains(entity)) throw new SaveException($"{name}: entity {entity} already has it");
        int k = c.Owners.FindIndex(o => o > entity);
        if (k < 0) k = c.Count;
        var old = c.Count > 0 ? Read(c.Offset, c.Count * c.Size) : [];
        byte[] arr = [.. old.AsSpan(0, k * c.Size), .. element, .. old.AsSpan(k * c.Size)];
        c.Offset = Append(arr);
        BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(c.EntryPos + 40), c.Offset);
        c.Owners.Insert(k, entity);
        c.Count++;
        var idx = Append(new byte[4 * c.Count]);
        BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(c.MapPos), idx);
        StoreCount(c);
    }

    /// <summary>Append an item to the range stored at <paramref name="off"/> (the items are moved to fresh space).</summary>
    public ulong RangeAppend(ulong off, byte[] item)
    {
        var (s, e) = Range(off);
        var old = s == Empty ? [] : Read(s, (int)(e - s));
        var ns = Append([.. old, .. item]);
        SetU64(off, ns);
        SetU64(off + 8, ns + (ulong)old.Length + (ulong)item.Length);
        return ns + (ulong)old.Length;
    }

    /// <summary>
    /// Append an element to a side table (no owners). The array moves, so <paramref name="pointers"/>, the offsets of
    /// u64 fields known to point into it, are rewritten. Returns the new element's offset.
    /// </summary>
    public ulong SideAppend(string name, byte[] element, IEnumerable<ulong> pointers)
    {
        var c = Comp(name);
        if (c.Owners != null) throw new SaveException($"{name} is not a side table");
        var oldOff = c.Offset;
        var oldEnd = oldOff + (ulong)(c.Count * c.Size);
        var old = c.Count > 0 ? Read(oldOff, c.Count * c.Size) : [];
        var ptrs = pointers.ToList();
        c.Offset = Append([.. old, .. element]);
        BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(c.EntryPos + 40), c.Offset);
        c.Count++;
        BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(c.EntryPos + 32), (ulong)c.Count);
        foreach (var p in ptrs)
        {
            var v = U64(p);
            if (v >= oldOff && v < oldEnd) SetU64(p, c.Offset + (v - oldOff));
        }
        return c.Offset + (ulong)old.Length;
    }

    /// <summary>Offset of a stored UTF-8 string equal to <paramref name="s"/> referenced by a component, or a fresh copy.</summary>
    public ulong StringFor(string s, IEnumerable<(ulong ptr, int len)> candidates)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        foreach (var (ptr, len) in candidates)
            if (len == bytes.Length && Read(ptr, len).AsSpan().SequenceEqual(bytes)) return ptr;
        return Append(bytes);
    }

    // --- ranges and references ------------------------------------------------------------------------------------

    public List<ulong> RangeItems(ulong off, int itemSize)
    {
        var (s, e) = Range(off);
        var items = new List<ulong>();
        if (s == Empty) return items;
        if ((e - s) % (ulong)itemSize != 0) throw new SaveException($"range @{off:x} is not a multiple of {itemSize}");
        for (var o = s; o < e; o += (ulong)itemSize) items.Add(o);
        return items;
    }

    /// <summary>Remove item <paramref name="index"/> from the range stored at <paramref name="off"/>, compacting.</summary>
    public void RangeRemove(ulong off, int itemSize, int index)
    {
        var (s, e) = Range(off);
        int n = (int)((e - s) / (ulong)itemSize);
        if (index < 0 || index >= n) throw new SaveException("index out of range");
        var rest = Read(s + (ulong)((index + 1) * itemSize), (n - index - 1) * itemSize);
        Write(s + (ulong)(index * itemSize), rest);
        Write(e - (ulong)itemSize, new byte[itemSize]);
        if (n == 1)
        {
            SetU64(off, Empty);
            SetU64(off + 8, Empty);
        }
        else SetU64(off + 8, e - (ulong)itemSize);
    }

    /// <summary>Entity reference (offset into the EntityId array) to entity index.</summary>
    public int? Ref(ulong off)
    {
        if (Base + off + 16 > (ulong)_data.Length) return null;
        return _entityIndex.TryGetValue(new Guid(Read(off, 16)), out var i) ? i : null;
    }

    /// <summary>Any EntityId slot holding this entity's GUID, usable as a reference to it.</summary>
    public ulong RefFor(int entity)
    {
        var c = Comp(EntityIdComponent);
        var guid = Entities[entity];
        for (int k = 0; k < c.Count; k++)
        {
            var off = ElementOffset(c, k);
            if (_data.AsSpan(Base + (int)off, 16).SequenceEqual(guid)) return off;
        }
        throw new SaveException($"entity {entity} has no EntityId entry");
    }

    // --- GUIDs ------------------------------------------------------------------------------------------------------

    /// <summary>LSMF GUID encoding (entities and player profiles): first 8 bytes Microsoft layout, last 8 swapped pairwise.</summary>
    public static string GuidString(ReadOnlySpan<byte> b)
    {
        Span<byte> t = stackalloc byte[8];
        for (int i = 0; i < 8; i += 2) { t[i] = b[8 + i + 1]; t[i + 1] = b[8 + i]; }
        static string Rev(ReadOnlySpan<byte> s) { var a = s.ToArray(); Array.Reverse(a); return Convert.ToHexStringLower(a); }
        return $"{Rev(b[..4])}-{Rev(b[4..6])}-{Rev(b[6..8])}-{Convert.ToHexStringLower(t[..2])}-{Convert.ToHexStringLower(t[2..])}";
    }

    public static byte[] GuidBytes(string s)
    {
        var h = Convert.FromHexString(s.Replace("-", ""));
        var r = new byte[16];
        r[0] = h[3]; r[1] = h[2]; r[2] = h[1]; r[3] = h[0];
        r[4] = h[5]; r[5] = h[4]; r[6] = h[7]; r[7] = h[6];
        for (int i = 8; i < 16; i += 2) { r[i] = h[i + 1]; r[i + 1] = h[i]; }
        return r;
    }
}

public sealed class SaveException(string message) : Exception(message);
