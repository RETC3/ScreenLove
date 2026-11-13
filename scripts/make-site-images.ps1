# Crea le immagini del sito in web/public con Edge headless:
#   og-image.png (anteprima per i social, 1200x630) da web/og/og-image.html
#   favicon.ico (16/32/48), apple-touch-icon.png (180), icon-192.png, icon-512.png e
#   icon-maskable-512.png da web/og/icon.html
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$og = Join-Path $root 'web\og'
$public = Join-Path $root 'web\public'
$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge non trovato' }
$tmp = Join-Path $env:TEMP "screenlove-images-$PID"
New-Item -ItemType Directory -Force $tmp | Out-Null

function Shot([string]$page, [string]$query, [int]$w, [int]$h, [string]$out) {
    $url = ([Uri](Join-Path $og $page)).AbsoluteUri + $query
    if (Test-Path $out) { Remove-Item $out }
    $ErrorActionPreference = 'Continue'   # Edge scrive su stderr anche quando va tutto bene
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
        --default-background-color=00000000 --window-size=$w,$h --virtual-time-budget=3000 `
        "--user-data-dir=$tmp\profile" "--screenshot=$out" $url 2>$null | Out-Null
    if (-not (Test-Path $out)) { throw "Screenshot non creato: $out" }
}

# Ridimensiona un PNG quadrato e restituisce i byte del PNG risultante.
function Resize([string]$src, [int]$size) {
    $img = [System.Drawing.Image]::FromFile($src)
    try {
        $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($img, 0, 0, $size, $size)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        return ,$ms.ToArray()
    } finally { $img.Dispose() }
}

# File .ico con dentro immagini PNG (supportato da tutti i browser e da Windows Vista in poi).
function Write-Ico([string]$out, [byte[][]]$pngs, [int[]]$sizes) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    for ($i = 0; $i -lt $pngs.Count; $i++) {
        $s = $sizes[$i]
        $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($out, $ms.ToArray())
}

try {
    Shot 'og-image.html' '' 1200 630 (Join-Path $public 'og-image.png')

    $rounded = Join-Path $tmp 'rounded.png'; Shot 'icon.html' '?v=rounded' 512 512 $rounded
    $full = Join-Path $tmp 'full.png'; Shot 'icon.html' '?v=full' 512 512 $full
    $maskable = Join-Path $tmp 'maskable.png'; Shot 'icon.html' '?v=maskable' 512 512 $maskable

    [System.IO.File]::WriteAllBytes((Join-Path $public 'apple-touch-icon.png'), (Resize $full 180))
    [System.IO.File]::WriteAllBytes((Join-Path $public 'icon-192.png'), (Resize $rounded 192))
    [System.IO.File]::WriteAllBytes((Join-Path $public 'icon-512.png'), (Resize $rounded 512))
    [System.IO.File]::WriteAllBytes((Join-Path $public 'icon-maskable-512.png'), (Resize $maskable 512))
    $sizes = 16, 32, 48
    Write-Ico (Join-Path $public 'favicon.ico') @($sizes | ForEach-Object { ,(Resize $rounded $_) }) $sizes

    'og-image.png', 'favicon.ico', 'apple-touch-icon.png', 'icon-192.png', 'icon-512.png', 'icon-maskable-512.png' |
        ForEach-Object { Write-Host ("{0,-24} {1,8} byte" -f $_, (Get-Item (Join-Path $public $_)).Length) }
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
