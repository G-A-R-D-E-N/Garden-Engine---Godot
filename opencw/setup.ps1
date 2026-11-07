# One-shot setup for Windows: installs the engine from a downloaded release zip, builds niftool and
# the C# project, then opens the editor. Safe to re-run; it only redoes what is missing or outdated.
param(
    [string]$EngineZip,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$ReleasesUrl = 'https://git.nomadicinteractive.dev/garden-engine/Garden_Engine_Source/releases'
$ZipPattern = 'godot-4.8.0-dev-double-mono-win64*.zip'
$EngineDir = Join-Path $Root 'engine'
$EngineExe = Join-Path $EngineDir 'godot.windows.editor.double.x86_64.mono.exe'
$Stamp = Join-Path $EngineDir '.installed-from'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "   $text" -ForegroundColor Green }
function Warn($text) { Write-Host "   $text" -ForegroundColor Yellow }
function Fail($text) { Write-Host "   $text" -ForegroundColor Red; exit 1 }

Step '.NET SDK'
function Has-Sdk10 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    [bool](dotnet --list-sdks | ForEach-Object { [int]($_ -split '\.')[0] } | Where-Object { $_ -ge 10 })
}
if (-not (Has-Sdk10)) {
    if ((Get-Command winget -ErrorAction SilentlyContinue) -and (Read-Host '   .NET SDK 10 not found. Install it with winget now? [y/N]') -eq 'y') {
        winget install --exact --id Microsoft.DotNet.SDK.10 --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE) { Fail "winget could not install the .NET SDK 10 (exit code $LASTEXITCODE). Install it from https://dotnet.microsoft.com/download, then run setup again." }
        $env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')
        if (-not (Has-Sdk10)) { Fail 'Installed, but the .NET SDK 10 is not on PATH yet. Open a new terminal and run setup again.' }
    } else {
        Fail 'Install the .NET SDK 10 from https://dotnet.microsoft.com/download, then run setup again.'
    }
}
Ok "found: $(dotnet --version)"

Step 'Engine'
if (-not $EngineZip) {
    $downloads = Join-Path $env:USERPROFILE 'Downloads'
    $EngineZip = Get-ChildItem $downloads -Filter $ZipPattern -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
$staging = Join-Path $Root 'engine.staging'
$previous = Join-Path $Root 'engine.previous'
if (-not (Test-Path $EngineExe) -and (Test-Path (Join-Path $previous 'godot.windows.editor.double.x86_64.mono.exe'))) {
    if (Test-Path $EngineDir) { Remove-Item $EngineDir -Recurse -Force }
    Move-Item -LiteralPath $previous -Destination $EngineDir
    Warn 'restored the engine left behind by an interrupted update'
}
foreach ($dir in $staging, $previous) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
$installed = if (Test-Path $Stamp) { Get-Content $Stamp -Raw } else { '' }
if ($EngineZip) {
    $zip = Get-Item $EngineZip
    $id = "$($zip.Name) $($zip.Length) $($zip.LastWriteTimeUtc.Ticks)"
    if ((Test-Path $EngineExe) -and $installed.Trim() -eq $id) {
        Ok "up to date ($($zip.Name))"
    } else {
        $sums = "$($zip.FullName).sha256"
        if (Test-Path $sums -PathType Leaf) {
            $expected = ((Get-Content $sums -Raw) -split '\s+')[0].ToLower()
            $stream = [IO.File]::OpenRead($zip.FullName)
            try { $actual = -join ([Security.Cryptography.SHA256]::Create().ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) } finally { $stream.Dispose() }
            if ($expected -ne $actual) { Fail "$($zip.Name) does not match $($zip.Name).sha256 - download it again." }
            Ok 'checksum matches'
        } else {
            Warn "no $($zip.Name).sha256 next to the zip; skipping the checksum check"
        }
        Ok "unpacking $($zip.Name)"
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [IO.Compression.ZipFile]::ExtractToDirectory($zip.FullName, $staging)
        } catch {
            if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
            Fail "could not unpack $($zip.Name) ($($_.Exception.Message)) - the current engine was left as it was."
        }
        if (-not (Test-Path (Join-Path $staging 'godot.windows.editor.double.x86_64.mono.exe'))) {
            Remove-Item $staging -Recurse -Force
            Fail "$($zip.Name) has no godot.windows.editor.double.x86_64.mono.exe in it - the current engine was left as it was."
        }
        Set-Content (Join-Path $staging '.installed-from') $id -NoNewline
        try {
            if (Test-Path $EngineDir) { Move-Item -LiteralPath $EngineDir -Destination $previous }
            Move-Item -LiteralPath $staging -Destination $EngineDir
        } catch {
            if ((Test-Path $previous) -and -not (Test-Path $EngineDir)) { Move-Item -LiteralPath $previous -Destination $EngineDir }
            if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
            Fail "could not swap in the new engine ($($_.Exception.Message)) - close the editor and run setup again."
        }
        if (Test-Path $previous) { Remove-Item $previous -Recurse -Force }
        Ok "installed to $EngineDir"
    }
} elseif (Test-Path $EngineExe) {
    Ok 'using the engine already installed (put a newer zip in Downloads to update it)'
} else {
    Start-Process $ReleasesUrl
    Fail "Download godot-4.8.0-dev-double-mono-win64.zip from the release page that just opened (newest at the top), then run setup again."
}

Step 'niftool'
$niftool = Join-Path $Root 'tools\niftool\build\windows\x64\release\niftool.exe'
if (Test-Path $niftool) {
    Ok 'already built'
} elseif (Get-Command xmake -ErrorAction SilentlyContinue) {
    Push-Location (Join-Path $Root 'tools\niftool')
    try { xmake -y; if ($LASTEXITCODE) { Fail 'xmake failed - see the output above.' } } finally { Pop-Location }
    Ok 'built'
} else {
    Warn 'xmake not found - skipping. The editor still opens; install xmake (https://xmake.io) for asset conversion.'
}

Step 'Radicle'
$cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not $cmake -and (Test-Path $vswhere)) {
    $cmake = & $vswhere -latest -find 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' | Select-Object -First 1
}
$radicle = Join-Path $Root 'native\radicle'
if ($cmake) {
    & $cmake -S $radicle -B "$radicle\build" | Out-Null
    if ($LASTEXITCODE) { Fail 'Radicle configure failed - see the output above.' }
    & $cmake --build "$radicle\build" --config Release --target radicle_c
    if ($LASTEXITCODE) { Fail 'Radicle build failed - see the output above.' }
    Ok 'built'
} elseif (Test-Path "$radicle\bin\Radicle.props") {
    Warn 'cmake not found - using the Radicle already built.'
} else {
    Fail 'Install Visual Studio 2022 with "Desktop development with C++" (it includes CMake) and Python 3, then run setup again.'
}

Step 'C# build'
dotnet build (Join-Path $Root 'OpenCommonwealth.csproj') --nologo -v q '-clp:ErrorsOnly;NoSummary'
if ($LASTEXITCODE) { Fail 'dotnet build failed - see the output above.' }
Ok 'built'

if ($NoLaunch) { Step 'Done'; exit 0 }
Step 'Opening the editor'
Start-Process $EngineExe -ArgumentList '-e', '--path', "`"$Root`"" -WorkingDirectory $EngineDir
Ok 'launched'
