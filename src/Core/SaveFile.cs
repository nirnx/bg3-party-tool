using System.Text.Json;
using System.Text.Json.Nodes;
using LSLib.LS;
using LSLib.LS.Enums;

namespace Bg3PartyTool.Core;

public sealed record Avatar
{
    public required int Entity { get; init; }
    public required string Guid { get; init; } // ECS entity GUID
    public required string Uuid { get; init; } // Osiris character UUID
    public required string Name { get; init; }
    public required string Profile { get; init; } // owning player profile ID
    public required int Xp { get; init; }
    public string Race { get; init; } = "";
    public string Classes { get; init; } = "";
    public int Level { get; init; }
    public bool IsHost { get; set; }
    public bool IsMe { get; set; }
    public List<string> Recruits { get; init; } = new();
    public int? PartyIndex { get; init; } // index in SaveInfo / PartyMetaData
}

/// <summary>
/// An unpacked BG3 save. A player's custom character (avatar) is spread over several places:
/// - Globals.lsf, NewAge blob (ECS): Avatar, Player, ClientControl and party.Member components mark it as a player
///   character; party.Composition and party.UserSnapshot tie it to the player's profile; recruit.Recruiter /
///   RecruitedBy record which companions it recruited.
/// - Globals.lsf, Characters region: the legacy character record (flags, owner profile).
/// - StorySave.bin (Osiris): DB_Players, DB_Avatars, DB_PartyMembers and friends.
/// - meta.lsf (PartyMetaData) and SaveInfo.json: what the load screen shows.
/// Removing an avatar strips all of that, so the character stays in the world as a plain NPC and the game sees a free
/// player slot, which is what lets a newly joining player go through character creation.
/// </summary>
public sealed class SaveFile
{
    const string AvatarC = "game.avatar.v0.AvatarComponent";
    const string PlayerC = "game.v0.PlayerComponent";
    const string ClientControlC = "game.v0.ClientControlComponent";
    const string PartyMemberC = "game.party.v0.MemberComponent";
    const string CompositionC = "game.party.v0.CompositionComponent";
    const string UserSnapshotC = "game.party.v0.UserSnapshotComponent";
    const string UserGroupSnapshotC = "game.party.v0.UserGroupSnapshot";
    const string OffStageC = "game.v0.OffStageComponent";
    const string InvisibilityC = "game.invisibility.v6.InvisibilityComponent";
    const string InvisibilitySourceC = "game.invisibility.v6.EInvisibilitySourceType";

    // esv::CharacterFlags the game sets on characters it took off stage (and Activated, which it clears)
    const ulong ActivatedFlag = 0x10, OffStageFlag = 0x20, IgnoresTriggersFlag = 0x1000000000;
    const ulong StageFlags = ActivatedFlag | OffStageFlag | IgnoresTriggersFlag;
    // EInvisibilitySourceType value on every off-stage character in reference saves
    const ulong OffStageInvisibilitySource = 4;
    const string RecruiterC = "game.recruit.v0.RecruiterComponent";
    const string RecruitedByC = "game.recruit.v0.RecruitedByComponent";
    const string DisplayNameC = "game.display_names.v2.Component";
    const string IsCustomC = "game.character_creation.v0.IsCustomComponent";
    const string OsirisTagsC = "game.tags.v0.OsirisComponent";
    // the AVATAR tag ("A Character Selected in Character Creation"); the engine derives tag::AvatarComponent from it,
    // and the join logic hands any such character without a connected user to the next player who joins
    const string AvatarTag = "306b9b05-1057-4770-aa17-01af21acd650";
    const string ExperienceC = "game.experience.v0.ExperienceComponent";

    /// <summary>Osiris databases that make a character count as a player avatar. History-type databases are kept.</summary>
    static readonly HashSet<string> StoryDbs =
    [
        "DB_Players", "DB_Avatars", "DB_PartyMembers", "DB_PartOfTheTeam", "DB_GLO_Playable",
        "DB_ActivePlayerMarker", "DB_CheckPartyForPlayer", "DB_GLO_Backgrounds_Players",
        "DB_GLO_AvatarKickableFlags_Assigned", "DB_OriginKickFromPartyFlags", "DB_AvatarHasFaction",
    ];

    // esv::CharacterFlags bits only player characters carry: IsPlayer, Multiplayer, InParty, HostControl, CharacterControl
    const ulong PlayerFlags = 0x1 | 0x2 | 0x4 | 0x8 | 0x2000;

    public string LsvPath { get; }
    public string SaveName { get; private set; }
    public string? CurrentLevel => _info["Current Level"]?.GetValue<string>();
    /// <summary>meta SaveGameType: 1 quicksave, 2 autosave, otherwise a manual save (listed by name in the Load menu).</summary>
    public int SaveType => FindAttr(_meta.Regions["MetaData"], "SaveGameType")?.Value is { } v ? Convert.ToInt32(v) : 0;
    public int SaveId => FindAttr(_meta.Regions["MetaData"], "SaveGameID")?.Value is { } v ? Convert.ToInt32(v) : 0;
    /// <summary>Folder-name prefix the game put before "__" (leader name and an id); kept when renaming.</summary>
    public string FolderPrefix
    {
        get
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(LsvPath)) ?? "";
            return parent.Contains("__") ? parent[..parent.IndexOf("__", StringComparison.Ordinal)] : SaveName;
        }
    }
    /// <summary>What the game's Load menu shows for this save.</summary>
    public string InGameName => SaveType switch { 1 => L.F("type.quick.name", SaveId), 2 => L.F("type.auto.name", SaveId), _ => SaveName };

    /// <summary>Turn a quick/auto save into a manual one, so the Load menu lists it under its own name.</summary>
    public void MakeManualSave()
    {
        if (FindAttr(_meta.Regions["MetaData"], "SaveGameType") is { } t) t.Value = Convert.ChangeType(0, t.Value!.GetType());
    }
    public string? GameVersion => _info["Game Version"]?.GetValue<string>();
    public List<Avatar> Avatars { get; private set; }

    readonly string _dir;
    readonly Resource _globals;
    readonly Resource _meta;
    readonly JsonObject _info;
    readonly Region _newAgeRegion;
    Lsmf _na;
    byte[] _story;
    Dictionary<int, byte[]> _memberIds = new();

    static readonly ResourceLoadParameters LoadParams = ResourceLoadParameters.FromGameVersion(Game.BaldursGate3);

    public SaveFile(string lsv, string workDir)
    {
        LsvPath = lsv;
        _dir = Path.Combine(workDir, "unpacked");
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        Directory.CreateDirectory(_dir);
        new Packager().UncompressPackage(lsv, _dir, null);

        _globals = ResourceUtils.LoadResource(Path.Combine(_dir, "Globals.lsf"), LoadParams);
        _meta = ResourceUtils.LoadResource(Path.Combine(_dir, "meta.lsf"), LoadParams);
        _info = JsonNode.Parse(File.ReadAllText(Path.Combine(_dir, "SaveInfo.json")))!.AsObject();
        _story = File.ReadAllBytes(Path.Combine(_dir, "StorySave.bin"));
        _newAgeRegion = _globals.Regions.TryGetValue("NewAge", out var r) ? r : throw new SaveException(L.T("sf.nonewage"));
        _na = new Lsmf((byte[])_newAgeRegion.Attributes["NewAge"].Value);
        SaveName = _info["Save Name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(lsv);
        Avatars = FindAvatars();
    }

    // --- reading ------------------------------------------------------------------------------------------------

    static Node Child(Node n, string id) =>
        n.Children.TryGetValue(id, out var l) && l.Count > 0 ? l[0] : throw new SaveException($"missing node {id}");

    static IEnumerable<Node> Children(Node n, string id) => n.Children.TryGetValue(id, out var l) ? l : [];

    static readonly NodeSerializationSettings Ser = MakeSer();

    static NodeSerializationSettings MakeSer()
    {
        var s = new NodeSerializationSettings();
        LoadParams.ToSerializationSettings(s);
        return s;
    }

    /// <summary>Attribute as the game/LSX writes it (GUIDs are byte-swapped in memory).</summary>
    static string? Attr(Node n, string id) => n.Attributes.TryGetValue(id, out var a) ? a.AsString(Ser) : null;

    static NodeAttribute? FindAttr(Node n, string id)
    {
        if (n.Attributes.TryGetValue(id, out var a)) return a;
        foreach (var list in n.Children.Values)
            foreach (var c in list)
                if (FindAttr(c, id) is { } found) return found;
        return null;
    }

    /// <summary>(Creator, Character) node pairs of the Characters region; both lists are in the same order.</summary>
    List<(Node creator, Node character)> CharacterRecords()
    {
        var factory = Child(_globals.Regions["Characters"], "CharacterFactory");
        var creators = Children(Child(factory, "Creators"), "Creator").ToList();
        var chars = Children(Child(factory, "Characters"), "Character").ToList();
        if (creators.Count != chars.Count) throw new SaveException("Characters region: Creator and Character counts differ");
        return creators.Zip(chars).ToList();
    }

    (Node creator, Node character) CharacterByGuid(string guid) =>
        CharacterRecords().FirstOrDefault(p => string.Equals(Attr(p.creator, "Entity"), guid, StringComparison.OrdinalIgnoreCase)) is var rec && rec.creator != null
            ? rec : throw new SaveException($"no Characters record for entity {guid}");

    public string DisplayName(int entity)
    {
        if (!_na.ByEntity(DisplayNameC).TryGetValue(entity, out var raw)) return $"encja {entity}";
        var s = BitConverter.ToUInt64(raw, 0);
        var e = BitConverter.ToUInt64(raw, 8);
        if (s == Lsmf.Empty) return $"encja {entity}";
        for (var ts = s; ts < e; ts += 32)
        {
            var rec = _na.Read(ts, 32);
            var ptr = BitConverter.ToUInt64(rec, 16);
            var len = BitConverter.ToUInt32(rec, 24);
            if (ptr != Lsmf.Empty && len is > 0 and < 256) return _na.String(ptr, (int)len);
        }
        return $"encja {entity}";
    }

    List<Avatar> FindAvatars()
    {
        var members = _na.ByEntity(PartyMemberC);
        var xp = _na.ByEntity(ExperienceC);
        var leader = FindAttr(_meta.Regions["MetaData"], "LeaderName")?.AsString(Ser);
        var party = _info["Active Party"]?["Characters"]?.AsArray() ?? new JsonArray();

        var recruits = new Dictionary<int, List<string>>();
        foreach (var (owner, raw) in _na.Elements(RecruitedByC))
            if (_na.Ref(BitConverter.ToUInt64(raw, 0)) is int by && owner is int o)
                (recruits.TryGetValue(by, out var l) ? l : recruits[by] = new()).Add(DisplayName(o));

        var avatars = new List<Avatar>();
        foreach (var entity in _na.Comp(AvatarC).Owners!)
        {
            var guid = Lsmf.GuidString(_na.Entities[entity]);
            var (creator, ch) = CharacterByGuid(guid);
            var pos = Attr(ch, "Translate")!.Split(' ').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

            // SaveInfo / PartyMetaData carry no IDs; match by position (equal XP is common in co-op)
            int? best = null;
            double bestDist = 0.5;
            for (int i = 0; i < party.Count; i++)
            {
                var p = party[i]?["Position"]?.AsArray();
                if (p == null || p.Count < 3) continue;
                var d = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(p[k]!.GetValue<double>() - pos[k], 2)));
                if (d < bestDist) { bestDist = d; best = i; }
            }
            var info = best is int b ? party[b] : null;
            avatars.Add(new Avatar
            {
                Entity = entity,
                Guid = guid,
                Uuid = Attr(creator, "UUID") ?? "",
                Name = DisplayName(entity),
                Profile = members.TryGetValue(entity, out var m) ? Lsmf.GuidString(m.AsSpan(0, 16)) : "",
                Xp = xp.TryGetValue(entity, out var x) ? BitConverter.ToInt32(x, 0) : 0,
                Recruits = recruits.GetValueOrDefault(entity) ?? new(),
                PartyIndex = best,
                Race = info?["Race"]?.GetValue<string>() ?? "",
                Level = info?["Level"]?.GetValue<int>() ?? 0,
                Classes = string.Join(", ", (info?["Classes"]?.AsArray() ?? new JsonArray()).Select(c =>
                {
                    var main = c?["Main"]?.GetValue<string>() ?? "?";
                    var sub = c?["Sub"]?.GetValue<string>();
                    return string.IsNullOrEmpty(sub) ? main : $"{main} ({sub})";
                })),
            });
        }
        var hostProfile = avatars.FirstOrDefault(a => a.Name == leader)?.Profile;
        foreach (var a in avatars) a.IsHost = hostProfile != null && a.Profile == hostProfile;
        return avatars;
    }

    public Avatar FindAvatar(string key) =>
        Avatars.FirstOrDefault(a => a.Name.Equals(key, StringComparison.OrdinalIgnoreCase) || a.Guid == key || a.Uuid == key)
        ?? throw new SaveException(L.F("sf.noavatar", key, string.Join(", ", Avatars.Select(a => a.Name))));

    // --- removing -----------------------------------------------------------------------------------------------

    /// <summary>Format of the .postac.json export; v2 carries everything <see cref="RestoreAvatar"/> needs.</summary>
    public const int ExportFormat = 2;

    public string? GameId => FindAttr(_meta.Regions["MetaData"], "GameID")?.AsString(Ser);

    /// <summary>Turn an avatar into a plain NPC. Returns an export record describing everything removed.</summary>
    public JsonObject RemoveAvatar(string key, string? recruitsTo, Action<string> log)
    {
        var a = FindAvatar(key);
        var others = Avatars.Where(o => o.Entity != a.Entity).ToList();
        if (others.Count == 0) throw new SaveException(L.T("sf.last"));
        var newage = new JsonObject();
        var export = new JsonObject
        {
            ["format"] = ExportFormat,
            ["game_id"] = GameId,
            ["avatar"] = JsonSerializer.SerializeToNode(a),
            ["save"] = SaveName,
            ["source"] = LsvPath,
            ["exported"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["newage"] = newage,
        };

        // 1. ECS tag components (member ids are needed for the composition lookup, so read them first)
        _memberIds = _na.Elements(PartyMemberC).Where(e => e.owner != null).ToDictionary(e => e.owner!.Value, e => e.raw[24..40]);
        if (_memberIds.TryGetValue(a.Entity, out var memberId)) export["member_id"] = Convert.ToHexStringLower(memberId);
        foreach (var comp in new[] { AvatarC, PlayerC, ClientControlC, PartyMemberC })
            if (Step('a') && _na.RemoveElement(comp, a.Entity) is { } old)
            {
                newage[comp] = Convert.ToHexStringLower(old);
                log(L.F("sf.removed", comp));
            }
        if (Step('a') && RemoveTag(a.Entity, AvatarTag))
        {
            export["tags_removed"] = new JsonArray(AvatarTag);
            log(L.T("sf.tag"));
        }
        if (Step('b')) RemoveFromComposition(a, newage, log);
        if (Step('c')) RemoveFromUserSnapshot(a, newage, log);

        // 2. companions recruited by this avatar go to another player
        if (a.Recruits.Count > 0 && Step('d'))
        {
            var target = recruitsTo != null ? FindAvatar(recruitsTo) : others.FirstOrDefault(o => o.IsHost) ?? others[0];
            MoveRecruits(a, target, log);
            export["recruits_moved_to"] = target.Name;
        }

        // 3. legacy Characters record
        var (_, ch) = CharacterByGuid(a.Guid);
        var flags = ch.Attributes["Flags"];
        var oldFlags = Convert.ToUInt64(flags.Value);
        if (Step('e'))
        {
            flags.Value = oldFlags & ~PlayerFlags;
            SetOwnerProfile(ch, "");
        }
        export["character_flags"] = oldFlags;
        log(L.F("sf.flags", oldFlags, oldFlags & ~PlayerFlags));
        if (Step('f')) export["hidden"] = Hide(a.Entity, ch, log);
        if (StepOpt('k') && _na.Has(IsCustomC) && _na.RemoveElement(IsCustomC, a.Entity) is { } custom)
        {
            // experimental: the game caps custom characters at 4; a parked one may still count
            newage[IsCustomC] = Convert.ToHexStringLower(custom);
            log(L.T("sf.iscustom"));
        }
        if (StepOpt('m'))
        {
            // experimental: a character-creation dummy reserved for a player who is not connected counts as a free
            // avatar and gets handed to whoever joins next; reserve the leaving player's dummies for the host instead
            var to = Environment.GetEnvironmentVariable("BG3PT_RESERVE_TO") is { Length: > 0 } env ? env
                : (others.FirstOrDefault(o => o.IsHost) ?? others[0]).Profile;
            var moved = new JsonArray();
            foreach (var (creator, character) in CharacterRecords())
                if (Attr(character, "Level") == "SYS_CC_I" && ReservedProfile(character) is var rp && (rp == a.Profile || string.IsNullOrEmpty(rp))
                    && PlayerCustomData(character, "ReservedProfileID").Any())
                {
                    moved.Add(new JsonObject { ["entity"] = Attr(creator, "Entity"), ["was"] = rp ?? "" });
                    SetReservedProfile(character, to);
                }
            if (moved.Count > 0)
            {
                export["dummy_reservations"] = moved;
                log(L.F("sf.dummies", moved.Count, to[..8]));
            }
        }
        else if (StepOpt('i') && !others.Any(o => o.Profile == a.Profile))
        {
            // the player's character-creation dummy (level SYS_CC_I) keeps a reservation for their profile; with the
            // player gone it would hold a party slot nobody can use
            var released = new JsonArray();
            foreach (var (creator, character) in CharacterRecords())
                if (ReservedProfile(character) == a.Profile && Attr(creator, "Entity") is { } guid && !guid.Equals(a.Guid, StringComparison.OrdinalIgnoreCase))
                {
                    SetReservedProfile(character, "");
                    released.Add(guid);
                }
            if (released.Count > 0)
            {
                export["released_reservations"] = released;
                log(L.F("sf.released", released.Count));
            }
        }

        // 4. load-screen metadata
        if (a.PartyIndex is int pi && Step('g'))
        {
            var partyMeta = FindNode(_meta.Regions["MetaData"], "PartyMetaData");
            if (partyMeta != null && partyMeta.Children.TryGetValue("CharacterMetaData", out var entries) && pi < entries.Count)
            {
                var node = entries[pi];
                var name = node.Attributes.TryGetValue("CharacterName", out var cn) ? cn.Value as TranslatedString : null;
                export["meta"] = new JsonObject
                {
                    ["icon"] = Attr(node, "CharacterIconID"),
                    ["name_handle"] = name?.Handle,
                    ["name_version"] = name?.Version ?? 0,
                };
                entries.RemoveAt(pi);
            }
            var chars = _info["Active Party"]!["Characters"]!.AsArray();
            export["saveinfo"] = chars[pi]!.DeepClone();
            chars.RemoveAt(pi);
            log(L.T("sf.meta.rm"));
        }

        // 5. Osiris story
        if (!string.IsNullOrEmpty(a.Uuid) && Step('h'))
        {
            var (story, removed) = StoryEditor.RemoveFacts(_story, a.Uuid, StoryDbs);
            _story = story;
            export["story"] = new JsonArray(removed.Select(r => (JsonNode)StoryEditor.ToJson(r)).ToArray());
            log(L.F("sf.osiris.rm", removed.Count, string.Join(", ", removed.Select(r => r.Db).Distinct().Order())));
        }

        // 6. "max players reached": the story sets it when players fill the party and only re-checks it on a few events,
        // so it would keep blocking companions and drop-in character creation for the freed slot
        if (Step('j') && others.Count < MaxPartySize && MaxPlayersFlag() is { } flag && flag.Value is true)
        {
            flag.Value = false;
            var (story, removed) = StoryEditor.RemoveFacts(_story, MaxPlayersFlagId, GlobalFlagDb);
            _story = story;
            if (removed.Count > 0) export["max_players_flag"] = StoryEditor.ToJson(removed[0]);
            log(L.T("sf.flag.rm"));
        }

        _newAgeRegion.Attributes["NewAge"].Value = _na.ToArray();
        Avatars = Avatars.Where(o => o.Entity != a.Entity).ToList();
        return export;
    }

    /// <summary>Diagnostics: BG3PT_STEPS limits removal to the listed steps (a tags, b composition, c user snapshot,
    /// d recruits, e character flags, f hide, g load-screen meta, h story, i character-creation reservation, j max-players flag). Unset runs everything.</summary>
    static bool Step(char c) => Environment.GetEnvironmentVariable("BG3PT_STEPS") is not { Length: > 0 } only || only.Contains(c);

    /// <summary>Opt-in experimental step: runs only when BG3PT_STEPS lists it explicitly.</summary>
    static bool StepOpt(char c) => Environment.GetEnvironmentVariable("BG3PT_STEPS") is { Length: > 0 } only && only.Contains(c);

    /// <summary>Remove a story tag (GUID) from the entity's Osiris tag list; false if it wasn't there.</summary>
    bool RemoveTag(int entity, string guid)
    {
        if (!_na.Has(OsirisTagsC) || _na.ElementOf(OsirisTagsC, entity) is not { } k) return false;
        var off = _na.ElementOffset(_na.Comp(OsirisTagsC), k);
        var items = _na.RangeItems(off, 16);
        var idx = items.FindIndex(o => Lsmf.GuidString(_na.Read(o, 16)) == guid);
        if (idx < 0) return false;
        _na.RangeRemove(off, 16, idx);
        return true;
    }

    void AddTag(int entity, string guid)
    {
        if (!_na.Has(OsirisTagsC) || _na.ElementOf(OsirisTagsC, entity) is not { } k) return;
        var off = _na.ElementOffset(_na.Comp(OsirisTagsC), k);
        if (_na.RangeItems(off, 16).Any(o => Lsmf.GuidString(_na.Read(o, 16)) == guid)) return;
        _na.RangeAppend(off, Lsmf.GuidBytes(guid));
    }

    static IEnumerable<NodeAttribute> PlayerCustomData(Node character, string id)
    {
        if (character.Children.TryGetValue("PlayerData", out var pd) && pd.Count > 0 && pd[0].Children.TryGetValue("PlayerCustomData", out var pcd))
            foreach (var n in pcd)
                if (n.Attributes.TryGetValue(id, out var at)) yield return at;
    }

    static string? ReservedProfile(Node character) => PlayerCustomData(character, "ReservedProfileID").Select(at => at.Value?.ToString()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    static void SetReservedProfile(Node character, string profile)
    {
        foreach (var at in PlayerCustomData(character, "ReservedProfileID")) at.Value = profile;
    }

    static void SetOwnerProfile(Node character, string profile)
    {
        if (character.Children.TryGetValue("PlayerData", out var pd) && pd.Count > 0 && pd[0].Children.TryGetValue("PlayerCustomData", out var pcd))
            foreach (var n in pcd)
                foreach (var id in new[] { "OwnerProfileID", "ReservedProfileID" })
                    if (n.Attributes.TryGetValue(id, out var at)) at.Value = profile;
    }

    /// <summary>Take a character off stage the way the game does: invisible, inert, out of everyone's way.</summary>
    JsonObject Hide(int entity, Node ch, Action<string> log)
    {
        var flags = ch.Attributes["Flags"];
        var before = Convert.ToUInt64(flags.Value);
        flags.Value = (before & ~ActivatedFlag) | OffStageFlag | IgnoresTriggersFlag;
        var result = new JsonObject { ["flags"] = before, ["offstage"] = false, ["invisibility"] = false };

        if (_na.Has(OffStageC) && _na.ElementOf(OffStageC, entity) == null)
        {
            _na.InsertElement(OffStageC, entity, [0]);
            result["offstage"] = true;
        }
        if (_na.Has(InvisibilityC) && _na.Has(InvisibilitySourceC) && _na.ElementOf(InvisibilityC, entity) == null)
        {
            // element: pointer to the source-type enum value, position, u8 1, padding
            var src = _na.Comp(InvisibilitySourceC);
            ulong? ptr = null;
            for (int k = 0; k < src.Count; k++)
                if (_na.U64(_na.ElementOffset(src, k)) == OffStageInvisibilitySource) ptr = _na.ElementOffset(src, k);
            if (ptr == null)
            {
                var inv = _na.Comp(InvisibilityC);
                ptr = _na.SideAppend(InvisibilitySourceC, BitConverter.GetBytes(OffStageInvisibilitySource),
                    Enumerable.Range(0, inv.Count).Select(k => _na.ElementOffset(inv, k)));
            }
            var pos = Attr(ch, "Translate")!.Split(' ').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            _na.InsertElement(InvisibilityC, entity,
                [.. BitConverter.GetBytes(ptr.Value), .. pos.SelectMany(BitConverter.GetBytes), 1, 0xae, 0x24, 0xff]);
            result["invisibility"] = true;
        }
        log(L.T("sf.hidden"));
        return result;
    }

    void Unhide(int entity, Node ch, JsonObject hidden, Action<string> log)
    {
        var flags = ch.Attributes["Flags"];
        var before = hidden["flags"]?.GetValue<ulong>() ?? ActivatedFlag;
        flags.Value = (Convert.ToUInt64(flags.Value) & ~StageFlags) | (before & StageFlags);
        if (hidden["offstage"]?.GetValue<bool>() == true && _na.Has(OffStageC)) _na.RemoveElement(OffStageC, entity);
        if (hidden["invisibility"]?.GetValue<bool>() == true && _na.Has(InvisibilityC)) _na.RemoveElement(InvisibilityC, entity);
        log(L.T("sf.unhidden"));
    }

    // --- restoring ----------------------------------------------------------------------------------------------

    public sealed record ParkedInfo(string Name, string Guid, string Profile, string Race, string Classes, int Level, string Exported, string Save);

    /// <summary>Why an export cannot be restored into this save, or null if it can.</summary>
    public string? CannotRestore(JsonObject export)
    {
        if (export["format"]?.GetValue<int>() != ExportFormat) return L.T("sf.cannot.old");
        var guid = export["avatar"]?["Guid"]?.GetValue<string>();
        if (guid == null || EntityByGuid(guid) is not int entity) return L.T("sf.cannot.gone");
        if (Avatars.Any(a => a.Entity == entity)) return L.T("sf.cannot.already");
        if (Avatars.Count >= 4) return L.T("sf.cannot.full");
        return null;
    }

    public static ParkedInfo Describe(JsonObject export)
    {
        var a = export["avatar"]!;
        return new ParkedInfo(a["Name"]!.GetValue<string>(), a["Guid"]!.GetValue<string>(), a["Profile"]!.GetValue<string>(),
            a["Race"]?.GetValue<string>() ?? "", a["Classes"]?.GetValue<string>() ?? "", a["Level"]?.GetValue<int>() ?? 0,
            export["exported"]?.GetValue<string>() ?? "", export["save"]?.GetValue<string>() ?? "");
    }

    int? EntityByGuid(string guid)
    {
        var b = Lsmf.GuidBytes(guid);
        var i = _na.Entities.FindIndex(e => e.AsSpan().SequenceEqual(b));
        return i < 0 ? null : i;
    }

    /// <summary>Make a parked character (an NPC removed earlier) a player character of <paramref name="profile"/> again.</summary>
    public void RestoreAvatar(JsonObject export, string profile, bool takeBackRecruits, Action<string> log)
    {
        if (CannotRestore(export) is { } why) throw new SaveException(why);
        var info = Describe(export);
        var entity = EntityByGuid(info.Guid)!.Value;
        var selfRef = _na.RefFor(entity);
        var profileBytes = Lsmf.GuidBytes(profile);
        var memberId = export["member_id"] is { } m ? Convert.FromHexString(m.GetValue<string>()) : System.Guid.NewGuid().ToByteArray();
        var template = _na.Elements(PartyMemberC).Select(e => e.raw).FirstOrDefault()
            ?? throw new SaveException(L.T("sf.nomember"));

        // 1. ECS tag components
        var avatarStrings = _na.Elements(AvatarC).Select(e => (BitConverter.ToUInt64(e.raw, 0), (int)BitConverter.ToUInt32(e.raw, 8)));
        var str = _na.StringFor(profile, avatarStrings);
        _na.InsertElement(AvatarC, entity, [.. BitConverter.GetBytes(str), .. BitConverter.GetBytes((uint)profile.Length), 0xff, 0xff, 0xff, 0xff]);
        _na.InsertElement(PlayerC, entity, [1]);
        _na.InsertElement(ClientControlC, entity, [1]);
        _na.InsertElement(PartyMemberC, entity, [.. profileBytes, .. template.AsSpan(16, 8), .. memberId, .. template.AsSpan(40, 8)]);
        log(L.T("sf.added"));
        foreach (var t in export["tags_removed"]?.AsArray().Select(n => n!.GetValue<string>()) ?? [AvatarTag]) AddTag(entity, t);
        if (export["newage"]?[IsCustomC] is { } custom && _na.Has(IsCustomC) && _na.ElementOf(IsCustomC, entity) == null)
            _na.InsertElement(IsCustomC, entity, Convert.FromHexString(custom.GetValue<string>()));

        // 2. party composition: (profile, member id, refs to the character)
        var comp = _na.Comp(CompositionC);
        if (comp.Count > 0)
        {
            var refs = _na.Append(BitConverter.GetBytes(selfRef));
            _na.RangeAppend(_na.ElementOffset(comp, 0) + 16, [.. profileBytes, .. memberId, .. BitConverter.GetBytes(refs), .. BitConverter.GetBytes(refs + 8)]);
            log(L.T("sf.comp.add"));
        }

        // 3. user snapshot: profile -> UserGroupSnapshot element -> sub-ranges of refs
        var us = _na.Comp(UserSnapshotC);
        if (us.Count > 0)
        {
            var b = _na.ElementOffset(us, 0);
            var refs = _na.Append(BitConverter.GetBytes(selfRef));
            byte[] sub = [.. BitConverter.GetBytes(refs), .. BitConverter.GetBytes(refs + 8)];
            var profiles = _na.RangeItems(b, 16);
            var i = profiles.FindIndex(p => _na.Read(p, 16).AsSpan().SequenceEqual(profileBytes));
            if (i >= 0)
            {
                _na.RangeAppend(_na.U64(_na.RangeItems(b + 16, 8)[i]), sub);
                log(L.T("sf.group.add"));
            }
            else
            {
                var subs = _na.Append(sub);
                var group = _na.SideAppend(UserGroupSnapshotC, [.. BitConverter.GetBytes(subs), .. BitConverter.GetBytes(subs + 16)], _na.RangeItems(b + 16, 8));
                _na.RangeAppend(b, profileBytes);
                _na.RangeAppend(b + 16, BitConverter.GetBytes(group));
                log(L.F("sf.profile.add", profile));
            }
        }

        if (takeBackRecruits) TakeBackRecruits(entity, selfRef, export, log);

        // 4. legacy Characters record
        var (_, ch) = CharacterByGuid(info.Guid);
        if (export["hidden"] is JsonObject hidden) Unhide(entity, ch, hidden, log);
        var flags = ch.Attributes["Flags"];
        var exported = export["character_flags"]?.GetValue<ulong>() ?? (0x1 | 0x2 | 0x2000);
        flags.Value = Convert.ToUInt64(flags.Value) | (exported & PlayerFlags);
        SetOwnerProfile(ch, profile);
        log(L.T("sf.flags.back"));
        if (export["released_reservations"] is JsonArray released && profile == info.Profile)
            foreach (var guid in released.Select(g => g!.GetValue<string>()))
                if (CharacterRecords().FirstOrDefault(r => string.Equals(Attr(r.creator, "Entity"), guid, StringComparison.OrdinalIgnoreCase)) is { creator: not null } rec)
                    SetReservedProfile(rec.character, profile);

        // 5. load-screen metadata
        var partyMeta = FindNode(_meta.Regions["MetaData"], "PartyMetaData");
        if (partyMeta != null && export["meta"] is JsonObject meta)
        {
            var node = new Node { Name = "CharacterMetaData", Parent = partyMeta };
            node.Attributes["CharacterIconID"] = new NodeAttribute(AttributeType.FixedString) { Value = meta["icon"]?.GetValue<string>() ?? "CustomIconSet" };
            node.Attributes["CharacterName"] = new NodeAttribute(AttributeType.TranslatedString)
            {
                Value = new TranslatedString { Handle = meta["name_handle"]?.GetValue<string>() ?? "", Version = meta["name_version"]?.GetValue<ushort>() ?? 0 },
            };
            partyMeta.AppendChild(node);
        }
        if (export["saveinfo"] is JsonNode si)
            (_info["Active Party"]!["Characters"] as JsonArray)?.Add(si.DeepClone());
        log(L.T("sf.meta.add"));

        // 6. Osiris story; avatar slot flags and factions may have gone to someone else meanwhile
        var facts = (export["story"] as JsonArray ?? new JsonArray()).Select(n => StoryEditor.FromJson(n!)).ToList();
        var (story, added, skipped) = StoryEditor.InsertFacts(_story, facts, (f, existing) =>
        {
            if (!SlotDbs.Contains(f.Db)) return null;
            var slot = string.Join("\t", f.Text.Split('\t').Skip(1));
            return existing.Any(e => string.Join("\t", e.Split('\t').Skip(1)) == slot) ? L.T("sf.slot.taken") : null;
        });
        _story = story;
        log(L.F("sf.osiris.add", added.Count) + (skipped.Count > 0 ? L.F("sf.osiris.skip", skipped.Count, string.Join("; ", skipped)) : ""));

        _newAgeRegion.Attributes["NewAge"].Value = _na.ToArray();
        Avatars = FindAvatars();

        // 7. back to a full party of players: set the flag again, as the story would
        if (export["max_players_flag"] is JsonNode mf && Avatars.Count >= MaxPartySize && MaxPlayersFlag() is { } flag && flag.Value is false)
        {
            flag.Value = true;
            var (withFlag, _, _) = StoryEditor.InsertFacts(_story, [StoryEditor.FromJson(mf)], (_, _) => null);
            _story = withFlag;
            log(L.T("sf.flag.add"));
        }
    }

    // GetMaxPartySize() of a normal campaign; the story's PROC_CheckPartyFull compares players + reserved slots with it
    const int MaxPartySize = 4;
    const string MaxPlayersFlagId = "823b5064-8aa4-c0b7-1b8c-657b46987ccd";
    static readonly HashSet<string> GlobalFlagDb = ["DB_GlobalFlag"];

    /// <summary>The global flag GEN_MaxPlayerCountReached in OsirisVariableHelper's flag map (null if never set).</summary>
    NodeAttribute? MaxPlayersFlag()
    {
        NodeAttribute? Walk(Node n)
        {
            if (n.Name == "Object" && string.Equals(Attr(n, "MapKey"), MaxPlayersFlagId, StringComparison.OrdinalIgnoreCase)
                && n.Children.TryGetValue("FlagContainerMap", out var fc) && fc.Count > 0 && fc[0].Attributes.TryGetValue("Flags", out var f))
                return f;
            foreach (var list in n.Children.Values)
                foreach (var c in list)
                    if (Walk(c) is { } found) return found;
            return null;
        }
        return _globals.Regions.TryGetValue("OsirisVariableHelper", out var r) ? Walk(r) : null;
    }

    /// <summary>Companions the character had recruited before it was parked, if they are still with the same new recruiter.</summary>
    public List<string> RecruitsToTakeBack(JsonObject export)
    {
        var names = export["avatar"]?["Recruits"]?.AsArray().Select(n => n!.GetValue<string>()).ToHashSet() ?? [];
        var movedTo = export["recruits_moved_to"]?.GetValue<string>();
        if (names.Count == 0 || movedTo == null) return [];
        var holder = Avatars.FirstOrDefault(a => a.Name == movedTo);
        return holder == null ? [] : holder.Recruits.Where(names.Contains).ToList();
    }

    void TakeBackRecruits(int entity, ulong selfRef, JsonObject export, Action<string> log)
    {
        var names = RecruitsToTakeBack(export);
        if (names.Count == 0) return;
        var holder = Avatars.First(a => a.Name == export["recruits_moved_to"]!.GetValue<string>());

        // RecruitedBy on each companion -> this character
        var by = _na.Comp(RecruitedByC);
        var companions = new List<int>();
        for (int k = 0; k < by.Count; k++)
        {
            var owner = by.Owners![k];
            var off = _na.ElementOffset(by, k);
            if (_na.Ref(_na.U64(off)) == holder.Entity && names.Contains(DisplayName(owner)))
            {
                _na.SetU64(off, selfRef);
                companions.Add(owner);
            }
        }

        // Recruiter lists: take the companions out of the holder's list, give this character its own
        var rec = _na.Comp(RecruiterC);
        var kh = _na.ElementOf(RecruiterC, holder.Entity)!.Value;
        var hOff = _na.ElementOffset(rec, kh);
        var items = _na.RangeItems(hOff, 8);
        var mine = new List<ulong>();
        for (int i = items.Count - 1; i >= 0; i--)
            if (_na.Ref(_na.U64(items[i])) is int c && companions.Contains(c))
            {
                mine.Insert(0, _na.U64(items[i]));
                _na.RangeRemove(hOff, 8, i);
            }
        if (_na.RangeItems(hOff, 8).Count == 0) _na.RemoveElement(RecruiterC, holder.Entity);
        var list = _na.Append(mine.SelectMany(BitConverter.GetBytes).ToArray());
        _na.InsertElement(RecruiterC, entity, [.. BitConverter.GetBytes(list), .. BitConverter.GetBytes(list + (ulong)(8 * mine.Count))]);
        log(L.F("sf.recruits.back", string.Join(", ", names), holder.Name));
    }

    /// <summary>Databases tying an avatar to one of the four avatar slots (kick flags, player faction).</summary>
    static readonly HashSet<string> SlotDbs = ["DB_GLO_AvatarKickableFlags_Assigned", "DB_OriginKickFromPartyFlags", "DB_AvatarHasFaction"];

    static Node? FindNode(Node n, string id)
    {
        if (n.Children.TryGetValue(id, out var l) && l.Count > 0) return l[0];
        foreach (var list in n.Children.Values)
            foreach (var c in list)
                if (FindNode(c, id) is { } f) return f;
        return null;
    }

    void RemoveFromComposition(Avatar a, JsonObject newage, Action<string> log)
    {
        var comp = _na.Comp(CompositionC);
        if (comp.Count == 0) return;
        var rng = _na.ElementOffset(comp, 0) + 16;
        var profile = Lsmf.GuidBytes(a.Profile);
        var items = _na.RangeItems(rng, 48);
        for (int i = 0; i < items.Count; i++)
        {
            var rec = _na.Read(items[i], 48);
            if (rec.AsSpan(0, 16).SequenceEqual(profile) && _memberIds.TryGetValue(a.Entity, out var mid) && rec.AsSpan(16, 16).SequenceEqual(mid))
            {
                newage[CompositionC] = Convert.ToHexStringLower(rec);
                _na.RangeRemove(rng, 48, i);
                log(L.T("sf.comp.rm"));
                return;
            }
        }
    }

    void RemoveFromUserSnapshot(Avatar a, JsonObject newage, Action<string> log)
    {
        var comp = _na.Comp(UserSnapshotC);
        if (comp.Count == 0) return;
        var b = _na.ElementOffset(comp, 0);
        var profiles = _na.RangeItems(b, 16);
        var groups = _na.RangeItems(b + 16, 8);
        var profile = Lsmf.GuidBytes(a.Profile);
        for (int i = 0; i < profiles.Count; i++)
        {
            if (!_na.Read(profiles[i], 16).AsSpan().SequenceEqual(profile)) continue;
            var group = _na.U64(groups[i]); // UserGroupSnapshot element: range of 16-byte sub-ranges of entity refs
            var subs = _na.RangeItems(group, 16);
            for (int j = 0; j < subs.Count; j++)
            {
                if (!_na.RangeItems(subs[j], 8).Any(r => _na.Ref(_na.U64(r)) == a.Entity)) continue;
                if (subs.Count == 1)
                {
                    _na.RangeRemove(b, 16, i);
                    _na.RangeRemove(b + 16, 8, i);
                    log(L.F("sf.profile.rm", a.Profile));
                }
                else
                {
                    _na.RangeRemove(group, 16, j);
                    log(L.T("sf.group.rm"));
                }
                newage[UserSnapshotC] = $"profile {i}, group entry {j}";
                return;
            }
        }
    }

    void MoveRecruits(Avatar a, Avatar target, Action<string> log)
    {
        var targetRef = _na.RefFor(target.Entity);
        var by = _na.Comp(RecruitedByC);
        for (int k = 0; k < by.Count; k++)
        {
            var off = _na.ElementOffset(by, k);
            if (_na.Ref(_na.U64(off)) == a.Entity) _na.SetU64(off, targetRef);
        }
        var rec = _na.Comp(RecruiterC);
        if (_na.ElementOf(RecruiterC, a.Entity) is not int ka) return;
        var ra = _na.Range(_na.ElementOffset(rec, ka));
        if (_na.ElementOf(RecruiterC, target.Entity) is not int kt)
            _na.SetOwner(RecruiterC, ka, target.Entity);
        else
        {
            var rtOff = _na.ElementOffset(rec, kt);
            var rt = _na.Range(rtOff);
            if (rt.end == ra.start) _na.SetU64(rtOff + 8, ra.end);
            else if (ra.end == rt.start) _na.SetU64(rtOff, ra.start);
            else throw new SaveException(L.F("sf.recruits.merge", a.Name, target.Name));
            _na.RemoveElement(RecruiterC, a.Entity);
        }
        log(L.F("sf.recruits.moved", string.Join(", ", a.Recruits), target.Name));
    }

    // --- writing ------------------------------------------------------------------------------------------------

    /// <summary>Write the edited save as &lt;outDir&gt;/&lt;prefix&gt;__&lt;newName&gt;/&lt;newName&gt;.lsv, like the game lays saves out.</summary>
    public string Write(string outDir, string newName)
    {
        _info["Save Name"] = newName;
        // diagnostics: the Load menu names saves by type + SaveGameID only, so test variants get their own number
        if (int.TryParse(Environment.GetEnvironmentVariable("BG3PT_SAVEID"), out var saveId))
            FindAttr(_meta.Regions["MetaData"], "SaveGameID")!.Value = saveId;
        File.WriteAllText(Path.Combine(_dir, "SaveInfo.json"),
            _info.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.WriteAllBytes(Path.Combine(_dir, "StorySave.bin"), _story);
        var p = ResourceConversionParameters.FromGameVersion(Game.BaldursGate3);
        p.LSF = (LSFVersion)7;
        ResourceUtils.SaveResource(_globals, Path.Combine(_dir, "Globals.lsf"), ResourceFormat.LSF, p);
        ResourceUtils.SaveResource(_meta, Path.Combine(_dir, "meta.lsf"), ResourceFormat.LSF, p);

        var oldPic = Path.Combine(_dir, SaveName + ".WebP");
        var newPic = Path.Combine(_dir, newName + ".WebP");
        if (File.Exists(oldPic) && oldPic != newPic) File.Move(oldPic, newPic, true);

        var parent = Path.GetFileName(Path.GetDirectoryName(LsvPath)) ?? "";
        var prefix = parent.Contains("__") ? parent[..parent.IndexOf("__", StringComparison.Ordinal)] : SaveName;
        var folder = Path.Combine(outDir, $"{prefix}__{newName}");
        Directory.CreateDirectory(folder);
        var lsv = Path.Combine(folder, newName + ".lsv");
        // Packager.CreatePackage filters on the absolute path (a ".local" component drops every file), so list files here.
        var build = new PackageBuildData { Version = PackageVersion.V18, Compression = CompressionMethod.Zstd, CompressionLevel = LSCompressionLevel.Default };
        var files = Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)
            .Select(f => (abs: f, rel: Path.GetRelativePath(_dir, f).Replace('\\', '/')))
            .OrderBy(f => f.rel.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        foreach (var (abs, rel) in files) build.Files.Add(PackageBuildInputFile.CreateFromFilesystem(abs, rel));
        using (var writer = PackageWriterFactory.Create(build, lsv)) writer.Write();
        StampHash(lsv, files.Select(f => f.abs));
        if (File.Exists(newPic)) File.Copy(newPic, Path.Combine(folder, newName + ".WebP"), true);
        SaveName = newName;
        return lsv;
    }

    /// <summary>The game flags a save as tampered unless the LSPK v18 header carries its content hash: MD5 over every
    /// file's uncompressed bytes, files ordered by lower-cased path, each digest byte plus one (checked against
    /// untouched saves from 4.1.1). LSLib leaves the field zero.</summary>
    static void StampHash(string lsv, IEnumerable<string> filesInHashOrder)
    {
        using var md5 = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.MD5);
        foreach (var f in filesInHashOrder) md5.AppendData(File.ReadAllBytes(f));
        var hash = md5.GetHashAndReset();
        for (var i = 0; i < hash.Length; i++) hash[i]++;
        using var fs = new FileStream(lsv, FileMode.Open, FileAccess.ReadWrite);
        var head = new byte[8];
        fs.ReadExactly(head);
        if (BitConverter.ToUInt32(head, 0) != 0x4B50534C || BitConverter.ToUInt32(head, 4) != 18)
            throw new SaveException("unexpected save package format");
        fs.Position = 0x16; // magic, version, file list offset (8), file list size (4), flags, priority
        fs.Write(hash);
    }
}
