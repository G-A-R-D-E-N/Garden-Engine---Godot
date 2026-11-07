#!/usr/bin/env bash
# One-shot setup for Linux: installs the engine from a downloaded release zip, builds niftool and the
# C# project, then opens the editor. Safe to re-run; it only redoes what is missing or outdated.
# Usage: ./setup.sh [path/to/godot-4.8.0-dev-double-mono-linux64.zip] [--no-launch]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASES_URL=https://git.nomadicinteractive.dev/garden-engine/Garden_Engine_Source/releases
ENGINE_DIR="$ROOT/engine"
ENGINE_EXE="$ENGINE_DIR/godot.linuxbsd.editor.double.x86_64.mono"
STAMP="$ENGINE_DIR/.installed-from"
ZIP="" LAUNCH=1
for a in "$@"; do case "$a" in --no-launch) LAUNCH=0 ;; *) ZIP="$a" ;; esac; done

step() { printf '\n\033[36m== %s\033[0m\n' "$1"; }
ok() { printf '   \033[32m%s\033[0m\n' "$1"; }
warn() { printf '   \033[33m%s\033[0m\n' "$1"; }
fail() { printf '   \033[31m%s\033[0m\n' "$1"; exit 1; }

step ".NET SDK"
if ! command -v dotnet >/dev/null || ! dotnet --list-sdks | awk -F. '$1>=10{f=1} END{exit !f}'; then
    fail "Install the .NET SDK 10 (https://dotnet.microsoft.com/download), then run setup again."
fi
ok "found: $(dotnet --version)"

step "Engine"
if [ -z "$ZIP" ]; then
    ZIP=$(ls -t "$HOME"/Downloads/godot-4.8.0-dev-double-mono-linux64*.zip 2>/dev/null | head -1 || true)
fi
staging="$ROOT/engine.staging" previous="$ROOT/engine.previous"
if [ ! -x "$ENGINE_EXE" ] && [ -f "$previous/$(basename "$ENGINE_EXE")" ]; then
    rm -rf "$ENGINE_DIR"
    mv "$previous" "$ENGINE_DIR"
    warn "restored the engine left behind by an interrupted update"
fi
rm -rf "$staging" "$previous"
installed=$(cat "$STAMP" 2>/dev/null || true)
if [ -n "$ZIP" ]; then
    id="$(basename "$ZIP") $(stat -c '%s %Y' "$ZIP")"
    if [ -x "$ENGINE_EXE" ] && [ "$installed" = "$id" ]; then
        ok "up to date ($(basename "$ZIP"))"
    else
        sums="$ZIP.sha256"
        if [ -f "$sums" ]; then
            [ "$(cut -d' ' -f1 "$sums")" = "$(sha256sum "$ZIP" | cut -d' ' -f1)" ] \
                || fail "$(basename "$ZIP") does not match $(basename "$sums") - download it again."
            ok "checksum matches"
        else
            warn "no $(basename "$ZIP").sha256 next to the zip; skipping the checksum check"
        fi
        ok "unpacking $(basename "$ZIP")"
        if command -v unzip >/dev/null; then unpack=(unzip -q "$ZIP" -d "$staging"); else unpack=(python3 -m zipfile -e "$ZIP" "$staging"); fi
        "${unpack[@]}" || { rm -rf "$staging"; fail "could not unpack $(basename "$ZIP") - the current engine was left as it was."; }
        [ -f "$staging/$(basename "$ENGINE_EXE")" ] || { rm -rf "$staging"; fail "$(basename "$ZIP") has no godot.linuxbsd.editor.double.x86_64.mono in it - the current engine was left as it was."; }
        chmod +x "$staging/$(basename "$ENGINE_EXE")"
        printf '%s' "$id" > "$staging/.installed-from"
        [ ! -d "$ENGINE_DIR" ] || mv "$ENGINE_DIR" "$previous"
        mv "$staging" "$ENGINE_DIR" || { [ ! -d "$previous" ] || mv "$previous" "$ENGINE_DIR"; fail "could not swap in the new engine."; }
        rm -rf "$previous"
        ok "installed to $ENGINE_DIR"
    fi
elif [ -x "$ENGINE_EXE" ]; then
    ok "using the engine already installed (put a newer zip in ~/Downloads to update it)"
else
    command -v xdg-open >/dev/null && xdg-open "$RELEASES_URL" >/dev/null 2>&1 || true
    fail "Download godot-4.8.0-dev-double-mono-linux64.zip from $RELEASES_URL (newest at the top), then run setup again."
fi

step "niftool"
if [ -x "$ROOT/tools/niftool/build/linux/x86_64/release/niftool" ]; then
    ok "already built"
elif command -v xmake >/dev/null; then
    (cd "$ROOT/tools/niftool" && xmake -y) || fail "xmake failed - see the output above."
    ok "built"
else
    warn "xmake not found - skipping. The editor still opens; install xmake (https://xmake.io) for asset conversion."
fi

step "Radicle"
RADICLE="$ROOT/native/radicle"
if command -v cmake >/dev/null; then
    cmake -S "$RADICLE" -B "$RADICLE/build" -DCMAKE_BUILD_TYPE=Release >/dev/null \
        || fail "Radicle configure failed - see the output above."
    cmake --build "$RADICLE/build" --config Release --target radicle_c --parallel \
        || fail "Radicle build failed - see the output above."
    ok "built"
elif [ -f "$RADICLE/bin/Radicle.props" ]; then
    warn "cmake not found - using the Radicle already built."
else
    fail "Install CMake 3.25+, a C++23 compiler (GCC 13+ or Clang 17+) and Python 3, then run setup again."
fi

step "C# build"
dotnet build "$ROOT/OpenCommonwealth.csproj" --nologo -v q "-clp:ErrorsOnly;NoSummary" || fail "dotnet build failed - see the output above."
ok "built"

[ "$LAUNCH" = 1 ] || { step "Done"; exit 0; }
step "Opening the editor"
(cd "$ENGINE_DIR" && nohup "$ENGINE_EXE" -e --path "$ROOT" >/dev/null 2>&1 &)
ok "launched"
