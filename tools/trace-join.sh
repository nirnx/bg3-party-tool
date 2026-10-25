#!/usr/bin/env bash
# Attaches gdb to the running BG3 host and logs its join/character-creation decisions to ~/bg3-trace.log.
# Stop with Ctrl-C (gdb detaches, the game keeps running).
set -euo pipefail
PID=$(for p in $(pgrep -x bg3); do echo "$(awk '/VmRSS/{print $2}' /proc/$p/status) $p"; done | sort -rn | head -1 | awk '{print $2}')
[ -n "${PID:-}" ] || { echo "nie znalazłem procesu bg3" >&2; exit 1; }
echo "host bg3 pid=$PID; log: ${BG3_TRACE_LOG:-$HOME/bg3-trace.log}"
exec env BG3_PID="$PID" BG3_TRACE_LOG="${BG3_TRACE_LOG:-$HOME/bg3-trace.log}" gdb -q -batch -x "$(dirname "$0")/trace-join.py"
