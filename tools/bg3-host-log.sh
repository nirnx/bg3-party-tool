#!/usr/bin/env bash
# Starts the normal game (real install, real Steam identity, normal profile) with the engine's system log enabled.
# The log files land in the game's bin/ as gold.<timestamp>.log and network.<timestamp>.log; they record what the
# server does with a joining player (JoiningProtocol, avatar assignment), which the UI never shows.
set -euo pipefail
GAME="${BG3_DIR:-/var/mnt/games/SteamLibrary/steamapps/common/Baldurs Gate 3}"
RUNTIME="${BG3_RUNTIME:-$(dirname "$GAME")/SteamLinuxRuntime_sniper}"
cd "$GAME/bin"
export SteamAppId=1086940 SteamGameId=1086940
exec "$RUNTIME/run" -- ./bg3 -syslog "$@"
