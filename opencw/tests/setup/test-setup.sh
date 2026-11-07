#!/usr/bin/env bash
# Regression tests for setup.sh: runs it against fake engine zips in a throwaway folder.
# Needs python3; dotnet is stubbed so only the engine handling is exercised. Usage: bash tests/setup/test-setup.sh
set -uo pipefail
EXE=godot.linuxbsd.editor.double.x86_64.mono
SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/setup.sh"
WORK="$(mktemp -d)"
ROOT="$WORK/opencw" ENGINE="$WORK/opencw/engine"
failures=0
trap 'rm -rf "$WORK"' EXIT

check() { if [ "$2" = 0 ]; then printf 'PASS %s\n' "$1"; else printf 'FAIL %s\n' "$1"; failures=$((failures+1)); fi; }
make_zip() {
    local src="$WORK/src-$1"; mkdir -p "$src"
    printf '%s' "$2" > "$src/${3:-$EXE}"
    (cd "$src" && python3 -m zipfile -c "$WORK/$1" .)
    echo "$WORK/$1"
}
write_sum() { echo "$(sha256sum "$2" | cut -d' ' -f1)  $(basename "$1")" > "$1.sha256"; }
run_setup() { local rc=0; HOME="$WORK" bash "$ROOT/setup.sh" --no-launch ${1:+"$1"} >"$WORK/last.log" 2>&1 || rc=$?; [ -z "${SETUP_TEST_DEBUG:-}" ] || cat "$WORK/last.log"; return $rc; }
engine_is() { [ "$(cat "$ENGINE/$EXE" 2>/dev/null)" = "$1" ] && [ ! -e "$ROOT/engine.staging" ] && [ ! -e "$ROOT/engine.previous" ]; }

mkdir -p "$ROOT/tools/niftool/build/linux/x86_64/release" "$WORK/Downloads"
touch "$ROOT/tools/niftool/build/linux/x86_64/release/niftool"
chmod +x "$ROOT/tools/niftool/build/linux/x86_64/release/niftool"
mkdir -p "$WORK/bin"
printf '#!/bin/sh\ncase "$1" in --list-sdks) echo "10.0.100 [/fake]" ;; --version) echo 10.0.100 ;; esac\n' > "$WORK/bin/dotnet"
chmod +x "$WORK/bin/dotnet"
printf '#!/bin/sh\nexit 0\n' > "$WORK/bin/cmake"
chmod +x "$WORK/bin/cmake"
export PATH="$WORK/bin:$PATH"
cp "$SCRIPT" "$ROOT/"

a=$(make_zip godot-4.8.0-dev-double-mono-linux64-a.zip "engine A"); write_sum "$a" "$a"
run_setup "$a" && engine_is "engine A"; check "clean install succeeds" $?

b=$(make_zip godot-4.8.0-dev-double-mono-linux64-b.zip "engine B"); write_sum "$b" "$b"
run_setup "$b" && engine_is "engine B"; check "update replaces an existing engine" $?

run_setup "$a" && engine_is "engine A"; check "selected zip uses its own checksum, not the newest one" $?

bad=$(make_zip godot-4.8.0-dev-double-mono-linux64-bad.zip "engine bad"); write_sum "$bad" "$b"
! run_setup "$bad" && engine_is "engine A"; check "checksum mismatch keeps the engine" $?

echo "not a zip" > "$WORK/godot-4.8.0-dev-double-mono-linux64-corrupt.zip"
! run_setup "$WORK/godot-4.8.0-dev-double-mono-linux64-corrupt.zip" && engine_is "engine A"; check "corrupt zip keeps the engine" $?

noexe=$(make_zip godot-4.8.0-dev-double-mono-linux64-noexe.zip "x" readme.txt)
! run_setup "$noexe" && engine_is "engine A"; check "zip without the editor keeps the engine" $?

mv "$ENGINE" "$ROOT/engine.previous"; mkdir "$ROOT/engine.staging"
run_setup && engine_is "engine A"; check "interrupted swap is recovered" $?

printf '#!/bin/sh\nexit 1\n' > "$WORK/bin/cmake"
! run_setup && ! grep -q "C# build" "$WORK/last.log"; check "failed Radicle build stops setup" $?

[ "$failures" = 0 ] || { echo "$failures test(s) failed"; exit 1; }
echo "all setup tests passed"
