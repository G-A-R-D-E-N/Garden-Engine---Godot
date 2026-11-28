param([Parameter(Mandatory)][string]$Source)
Add-Type -AssemblyName System.Drawing

function New-Square([System.Drawing.Image]$src, [int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $pad = [int]($S * 0.04)
    $scale = ($S - 2 * $pad) / [Math]::Max($src.Width, $src.Height)
    $w = [int]($src.Width * $scale); $h = [int]($src.Height * $scale)
    $g.DrawImage($src, [int](($S - $w) / 2), [int](($S - $h) / 2), $w, $h)
    $g.Dispose()
    $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
(New-Square $src 256).Save((Join-Path $here 'Assets/AppIcon.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = 16, 32, 48, 64, 128, 256
$pngs = $sizes | ForEach-Object { , (Get-PngBytes (New-Square $src $_)) }
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $here 'Icon.ico'), $ms.ToArray())
$src.Dispose()
