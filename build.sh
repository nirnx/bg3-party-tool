#!/usr/bin/env bash
# Builds single-file, self-contained binaries into dist/ (no .NET needed on the target machine).
set -euo pipefail
cd "$(dirname "$0")"
for rid in linux-x64 win-x64; do
  dotnet publish src/Bg3PartyTool.csproj -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "dist/$rid"
done
ls -l dist/*/
