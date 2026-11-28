#!/usr/bin/env bash
# Builds the standalone dev launcher (Avalonia, cross-platform - see build.bat for the Windows
# equivalent) and drops it at the repo root as `dev`. Self-contained + single-file, so a fresh
# clone can run it with no .NET installed.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "[X] .NET SDK not found on PATH. Install .NET SDK 10+: https://dotnet.microsoft.com/download"
    exit 1
fi

case "${1:-linux}" in
    linux) RID=linux-x64; OUT=dev ;;
    windows|win) RID=win-x64; OUT=dev.exe ;;
    both) "$0" linux && exec "$0" windows ;;
    *) echo "[X] Unknown target '$1'. Use: linux | windows | both"; exit 1 ;;
esac

echo "Publishing $OUT ..."
dotnet publish "$HERE/OpenCommonwealth.Launcher.csproj" -c Release -r "$RID" -o "$HERE/publish/$RID" --nologo

cp -f "$HERE/publish/$RID/$OUT" "$ROOT/$OUT"
chmod +x "$ROOT/$OUT"

echo
echo "[OK] Built: $ROOT/$OUT"
if [ "$OUT" = dev ]; then echo "     Run it from the repo root:  ./dev"; fi
