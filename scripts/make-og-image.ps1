# Crea web/public/og-image.png (anteprima per i social, 1200x630) da web/og/og-image.html con Edge headless.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'web\og\og-image.html'
$out = Join-Path $root 'web\public\og-image.png'
$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge non trovato' }

$profile = Join-Path $env:TEMP "screenlove-og-$PID"
$url = ([Uri]$src).AbsoluteUri
& $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
    --window-size=1200,630 --virtual-time-budget=3000 "--user-data-dir=$profile" `
    "--screenshot=$out" $url | Out-Null
Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue
if (-not (Test-Path $out)) { throw 'Screenshot non creato' }
Write-Host "Creato $out ($((Get-Item $out).Length) byte)"
