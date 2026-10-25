#!/usr/bin/env bash
# Starts a second BG3 instance with its own Larian profile AND its own (fake) Steam identity, so it joins a session
# as a brand-new player. Use to test what a joining player gets (character creation or an existing character) without
# a second PC. The first instance hosts via Steam as usual; this one connects via Direct Connection (LAN).
#
# The second instance runs from a copy of the game's bin/ in $BG3_P2_DATA/game, where libsteam_api.so is the Goldberg
# emulator (gbe_fork) with its own account id: through the real Steam client both instances would share one Steam ID,
# which the game's drop-in character creation keys on. The original install is untouched.
set -euo pipefail
GAME="${BG3_DIR:-/var/mnt/games/SteamLibrary/steamapps/common/Baldurs Gate 3}"
RUNTIME="${BG3_RUNTIME:-$(dirname "$GAME")/SteamLinuxRuntime_sniper}"
DATA="${BG3_P2_DATA:-$HOME/.local/share/bg3-drugi-gracz}"
MAIN="${XDG_DATA_HOME:-$HOME/.local/share}/Larian Studios/Baldur's Gate 3"
P2="$DATA/Larian Studios/Baldur's Gate 3"

# same mods as the host, or the game refuses to join
mkdir -p "$P2/PlayerProfiles/Public"
rsync -a --delete "$MAIN/Mods/" "$P2/Mods/"
[ -f "$MAIN/PlayerProfiles/Public/modsettings.lsx" ] && cp "$MAIN/PlayerProfiles/Public/modsettings.lsx" "$P2/PlayerProfiles/Public/"
[ -f "$MAIN/graphicSettings.lsx" ] && [ ! -f "$P2/graphicSettings.lsx" ] && cp "$MAIN/graphicSettings.lsx" "$P2/"

BIN="$DATA/game/bin"
[ -f "$BIN/steam_settings/configs.user.ini" ] || { echo "brak kopii gry z emulatorem Steam w $BIN" >&2; exit 1; }
cd "$BIN"
export SteamAppId=1086940 SteamGameId=1086940 XDG_DATA_HOME="$DATA"
exec "$RUNTIME/run" -- ./bg3 "$@"
