# BG3 Party Tool

*English · [Polski](README.pl.md)*

Parks a player's character in a Baldur's Gate 3 save so that a new person joining your co-op campaign gets
**character creation** instead of being handed somebody else's character, and restores the parked character later.

The game itself has no way to do this: a character sent to Withers' Wardrobe is still a player character, and the next
person who joins is assigned it. This tool edits the save instead. The original save is never modified; every
operation writes a new save.

## Download

Grab the latest build from [Releases](https://github.com/nirnx/bg3-party-tool/releases): `BG3PartyTool-win-x64.zip`
(Windows) or `BG3PartyTool-linux-x64.tar.gz` (Linux). Single file, nothing to install.

## How to use

1. Start the tool. Your BG3 saves are listed on the left (the game folder is found automatically on Windows and Linux,
   including the native Linux build, Proton and Heroic/Wine prefixes). You can also drop a `.lsv` or a `.rar`/`.zip`
   with saves onto the window, or use **Add from disk…**.
2. Pick the save and click **Park character** on the player who is leaving. The new save goes straight into your BG3
   saves (or into the tool's folder, if someone else hosts and you need to send it to them). The character's data is
   written to `<Name>.postac.json`, which appears under **Parked characters**.
3. Load the new save in multiplayer. The newcomer joins and gets character creation.
4. When the old player comes back, open **Parked characters → Restore**. The character can be given back to the original
   player or to any player who already has a character in the save. A character file from another computer can be
   loaded with **Load character from file…**.

**The one rule to remember:** a newcomer gets character creation only when there is no free player character left in
the save. The game hands a joiner any player character whose owner is not connected, any character sitting in Withers'
Wardrobe, and the second character of a player who controls two. So, for the duration of character creation, every
remaining character must belong to a connected player and every player must control exactly one. If the host plays two
characters, park the second one as well and restore it afterwards. Companions in the active party must leave room too
(the game dismisses one automatically if the party is full).

Results are written as manual saves, so the game's Load menu lists them under their own name
(`QuickSave_12_without_Vesania` by default; you can type any name). The tool can also rename and delete saves, and the
UI is available in English and Polish (switch in the top-left corner).

Restoring works within one campaign, because the parked character still exists in that world. Moving a character to a
different campaign is not supported.

Tool data (imported saves, results, character files): `%LOCALAPPDATA%\BG3PartyTool` on Windows,
`~/.local/share/BG3PartyTool` on Linux.

### Command line

```
BG3PartyTool --list <save.lsv>
BG3PartyTool --import <file.lsv|.rar|.zip>
BG3PartyTool --remove <save.lsv> <character> [recruits-to] [--to-game]
BG3PartyTool --exports <save.lsv>
BG3PartyTool --restore <save.lsv> <character.postac.json> [profile] [--to-game]
BG3PartyTool --rename <save.lsv> <name>
BG3PartyTool --delete <save.lsv>
BG3PartyTool --game-dir
```

## What exactly is changed

The parked character stays in the world as a hidden NPC (off-stage and invisible, the way the game hides its own
characters), with its inventory and history intact. What is removed is everything that makes it a *player* character:

| Where | Change |
|---|---|
| `Globals.lsf` → ECS blob `NewAge` (LSMF) | the `AvatarComponent`, `PlayerComponent`, `ClientControlComponent` and `party.MemberComponent` elements; the entry in `party.CompositionComponent`; the player's profile in `party.UserSnapshotComponent`; the **AVATAR** tag in `tags.OsirisComponent`; companions recruited by the character (`recruit.RecruitedBy`/`Recruiter`) move to another player |
| `Globals.lsf` → `Characters` region | player flags (IsPlayer, Multiplayer, InParty, HostControl, CharacterControl) and the owner profile; the global flag `GEN_MaxPlayerCountReached` is cleared |
| `StorySave.bin` (Osiris) | the character's facts in `DB_Players`, `DB_Avatars`, `DB_PartyMembers`, `DB_PartOfTheTeam`, `DB_GLO_Playable` and a few related databases |
| `meta.lsf`, `SaveInfo.json` | the load-screen party entry |

Everything removed is stored in `<Name>.postac.json` (ECS elements, party member id, load-screen entry, the Osiris facts
as the game's original bytes, the campaign `GameID`), and restoring puts it back. A park-and-restore round trip yields a
save that is semantically identical to the original.

### Why the AVATAR tag matters

Traced with a debugger attached to the host: when someone joins, `esv::JoiningProtocol` collects candidates with the
`tag::AvatarComponent`, which the engine derives from the story tag **AVATAR** (`306b9b05-1057-4770-aa17-01af21acd650`),
not from the persisted `AvatarComponent`. Any candidate without a connected owner is handed to the joiner as a broken
NPC and no character creation starts. Only when no candidate is left does the server enqueue the user into
`esv::character_creation::System`. The same root cause explains the "removed player can only spectate" problem of the
old Script Extender console recipe for deleting characters.

### Checksums the game verifies

* **Package MD5** (LSPK v18 header): MD5 over the uncompressed contents of all files, sorted by lower-cased path, with
  each digest byte incremented by one. LSLib writes zeros there, which triggers the "save tampered or corrupted"
  warning; the tool stamps the real hash after packing.
* **NewAge blob hash** (LSMF header, offset 8): MurmurHash3 x64_128 over the bytes after the header
  (`blockOffset + blockSize`), seed `0xf0015eed5eedf001` in both halves, result `h1 + h2`. Without it the game aborts
  loading with "server shutdown". Recomputed on every write and checked when a save is opened.

Blob edits never move data between components (elements are shift-deleted, ranges compacted in place, new data appended
at the end with the header offsets updated), so no pointer breaks. Osiris facts are cut out byte-exact from the original
file; LSLib's writer is used only to locate them, because it encodes value flags differently from the game.

Tested with game version 4.1.1.x (LSMF 0x08000101). Other blob versions are rejected with a message.

## Building

Requires the .NET 10 SDK. `./build.sh` publishes both single-file binaries (cross-compiling the Windows one from Linux
works); GitHub Actions does the same on every push and publishes a release for `v*` tags.

`vendor/lslib` holds the assemblies from [LSLib](https://github.com/Norbyte/lslib) v1.20.4 (MIT). The Cinzel font is
under the SIL OFL (`src/Assets/Cinzel-OFL.txt`).

LSLib notes: `Packager.CreatePackage` filters files by their absolute path (a directory with a dot, e.g. `.local`, drops
everything) and adds nothing with `ExcludeHidden=false`, so the tool builds the package file list itself.

### Diagnostics

* `BG3PT_STEPS=abc…` limits parking to selected steps, `BG3PT_SAVEID=n` changes the save's number, `--repack` rewrites a
  save without changes.
* `tools/bg3-drugi-gracz.sh` starts a second instance of the game with its own profile (and its own Steam identity via a
  Steam API emulator placed in a copy of `bin/`) to test joining on one machine.
* `tools/bg3-host-log.sh` starts the host with the engine log; the native Linux build writes logs to
  `~/.local/state/Larian Studios/Baldur's Gate 3/logs/`.
* `tools/trace-join.sh` attaches gdb to the running host and logs the join and character-creation decisions
  (addresses for build 4.1.1.7631656).

## Licence

MIT, see `LICENSE`.
