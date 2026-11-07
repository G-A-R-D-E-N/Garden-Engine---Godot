# Regression tests for setup.ps1: runs it against fake engine zips in a throwaway folder.
# dotnet is stubbed so only the engine handling is exercised. Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tests\setup\test-setup.ps1
$ErrorActionPreference = 'Stop'
$Exe = 'godot.windows.editor.double.x86_64.mono.exe'
$Script = Join-Path $PSScriptRoot '..\..\setup.ps1' | Resolve-Path
$Work = Join-Path ([IO.Path]::GetTempPath()) "setup-test-$PID"
$Root = Join-Path $Work 'opencw'
$Engine = Join-Path $Root 'engine'
$script:failures = 0

function Check($name, $cond) {
    if ($cond) { Write-Host "PASS $name" -ForegroundColor Green } else { Write-Host "FAIL $name" -ForegroundColor Red; $script:failures++ }
}
function Make-Zip($name, $content, [switch]$NoExe, [switch]$Corrupt) {
    $path = Join-Path $Work $name
    if ($Corrupt) { Set-Content $path 'not a zip'; return $path }
    $src = Join-Path $Work "src-$name"
    New-Item -ItemType Directory $src -Force | Out-Null
    $file = if ($NoExe) { 'readme.txt' } else { $Exe }
    Set-Content (Join-Path $src $file) $content -NoNewline
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($src, $path)
    return $path
}
function Write-Sum($zip, $hashOf) {
    $stream = [IO.File]::OpenRead($hashOf)
    try { $h = -join ([Security.Cryptography.SHA256]::Create().ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) } finally { $stream.Dispose() }
    Set-Content "$zip.sha256" "$h  $(Split-Path $zip -Leaf)"
}
function Run-Setup($zip) {
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Root 'setup.ps1'), '-NoLaunch')
    if ($zip) { $a += '-EngineZip', $zip }
    & powershell.exe @a *> (Join-Path $Work 'last.log'); if ($env:SETUP_TEST_DEBUG) { Get-Content (Join-Path $Work 'last.log') | Write-Host }
    return $LASTEXITCODE
}
function Engine-Content { $p = Join-Path $Engine $Exe; if (Test-Path $p) { Get-Content $p -Raw } else { $null } }
function No-Leftovers { -not (Test-Path "$Root\engine.staging") -and -not (Test-Path "$Root\engine.previous") }

New-Item -ItemType Directory "$Root\tools\niftool\build\windows\x64\release", "$Work\Downloads" -Force | Out-Null
Set-Content "$Root\tools\niftool\build\windows\x64\release\niftool.exe" ''
New-Item -ItemType Directory "$Work\bin" | Out-Null
Set-Content "$Work\bin\dotnet.cmd" "@if `"%1`"==`"--list-sdks`" echo 10.0.100 [C:\fake]`r`n@if `"%1`"==`"--version`" echo 10.0.100`r`n@exit /b 0"
Set-Content "$Work\bin\cmake.cmd" "@exit /b 0"
$env:PATH = "$Work\bin;$env:PATH"
Copy-Item $Script $Root
$env:USERPROFILE = $Work

try {
    $a = Make-Zip 'godot-4.8.0-dev-double-mono-win64-a.zip' 'engine A'
    Write-Sum $a $a
    Check 'clean install succeeds' ((Run-Setup $a) -eq 0 -and (Engine-Content) -eq 'engine A' -and (No-Leftovers))

    $b = Make-Zip 'godot-4.8.0-dev-double-mono-win64-b.zip' 'engine B'
    Write-Sum $b $b
    Check 'update replaces an existing engine' ((Run-Setup $b) -eq 0 -and (Engine-Content) -eq 'engine B' -and (No-Leftovers))

    Check 'selected zip uses its own checksum, not the newest one' ((Run-Setup $a) -eq 0 -and (Engine-Content) -eq 'engine A')

    $bad = Make-Zip 'godot-4.8.0-dev-double-mono-win64-bad.zip' 'engine bad'
    Write-Sum $bad $b
    Check 'checksum mismatch keeps the engine' ((Run-Setup $bad) -ne 0 -and (Engine-Content) -eq 'engine A' -and (No-Leftovers))

    $corrupt = Make-Zip 'godot-4.8.0-dev-double-mono-win64-corrupt.zip' '' -Corrupt
    Check 'corrupt zip keeps the engine' ((Run-Setup $corrupt) -ne 0 -and (Engine-Content) -eq 'engine A' -and (No-Leftovers))

    $noexe = Make-Zip 'godot-4.8.0-dev-double-mono-win64-noexe.zip' 'x' -NoExe
    Check 'zip without the editor keeps the engine' ((Run-Setup $noexe) -ne 0 -and (Engine-Content) -eq 'engine A' -and (No-Leftovers))

    Move-Item $Engine "$Root\engine.previous"
    New-Item -ItemType Directory "$Root\engine.staging" | Out-Null
    Check 'interrupted swap is recovered' ((Run-Setup $null) -eq 0 -and (Engine-Content) -eq 'engine A' -and (No-Leftovers))

    Set-Content "$Work\bin\cmake.cmd" "@exit /b 1"
    $out = if ((Run-Setup $null) -ne 0) { Get-Content (Join-Path $Work 'last.log') -Raw } else { 'C# build' }
    Check 'failed Radicle build stops setup' ($out -notmatch 'C# build')
    Set-Content "$Work\bin\cmake.cmd" "@exit /b 0"

    New-Item -ItemType Directory "$Work\bin7" | Out-Null
    Set-Content "$Work\bin7\dotnet.cmd" "@if `"%1`"==`"--list-sdks`" echo 7.0.410 [C:\fake]`r`n@if `"%1`"==`"--version`" echo 7.0.410`r`n@exit /b 0"
    Set-Content "$Work\bin7\winget.cmd" "@exit /b 1"
    $savedPath = $env:PATH
    $env:PATH = "$Work\bin7;$savedPath"
    $log = Join-Path $Work 'last.log'
    'y' | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'setup.ps1') -NoLaunch *> $log
    $rc = $LASTEXITCODE
    $env:PATH = $savedPath
    $out = Get-Content $log -Raw
    Check 'failed winget install stops setup' ($rc -ne 0 -and $out -match 'winget could not install' -and $out -notmatch 'C# build')
} finally {
    Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($script:failures) { Write-Host "$script:failures test(s) failed" -ForegroundColor Red; exit 1 }
Write-Host 'all setup tests passed' -ForegroundColor Green
