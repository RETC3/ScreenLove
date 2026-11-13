# Crea web/public/latest.json dall'ultima release pubblicata su GitHub.
# Stesso formato dell'API di GitHub (/releases/latest), con due differenze:
#   - i link di download puntano al sito (https://screenlove.pages.dev/download/<file>)
#   - ogni file ha la sua impronta "sha256" (letta da SHA256SUMS.txt della release)
# L'app legge questo file per gli aggiornamenti; la pagina del sito per versione, dimensioni e novità.
param(
    [string]$Repo = 'RETC3/ScreenLove',
    [string]$Site = 'https://screenlove.pages.dev',
    [string]$Out = "$PSScriptRoot\..\web\public\latest.json",
    [string]$Tag  # facoltativo: una release precisa invece dell'ultima
)
$ErrorActionPreference = 'Stop'

$headers = @{ 'User-Agent' = 'ScreenLove-Site'; 'Accept' = 'application/vnd.github+json' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }

$url = if ($Tag) { "https://api.github.com/repos/$Repo/releases/tags/$Tag" } else { "https://api.github.com/repos/$Repo/releases/latest" }
$release = Invoke-RestMethod -Uri $url -Headers $headers

# impronte dei file
$sums = @{}
$sumsAsset = $release.assets | Where-Object name -eq 'SHA256SUMS.txt' | Select-Object -First 1
if ($sumsAsset) {
    $text = (Invoke-WebRequest -Uri $sumsAsset.browser_download_url -Headers @{ 'User-Agent' = 'ScreenLove-Site' } -UseBasicParsing).Content
    if ($text -is [byte[]]) { $text = [Text.Encoding]::UTF8.GetString($text) }
    foreach ($line in $text -split "`n") {
        if ($line -match '^([0-9a-fA-F]{64})\s+\*?(\S+)\s*$') { $sums[$Matches[2]] = $Matches[1].ToLower() }
    }
}

$assets = @()
foreach ($a in $release.assets) {
    if ($a.name -notmatch '^ScreenLove-.*\.(exe|zip)$') { continue }
    $entry = [ordered]@{
        name = $a.name
        size = $a.size
        browser_download_url = "$Site/download/$($a.name)"
    }
    if ($sums.ContainsKey($a.name)) { $entry.sha256 = $sums[$a.name] }
    $assets += [pscustomobject]$entry
}
if (-not ($assets | Where-Object name -like 'ScreenLove-Setup-*.exe')) { throw "La release $($release.tag_name) non contiene l'installer." }

$latest = [ordered]@{
    tag_name     = $release.tag_name
    name         = $release.name
    # PowerShell 7 converte le date in DateTime: si riscrivono nel formato ISO di GitHub
    published_at = if ($release.published_at -is [datetime]) { $release.published_at.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') } else { $release.published_at }
    html_url     = "$Site/#scarica"
    # la riga "Full Changelog" generata da GitHub non serve a chi usa l'app
    body         = (($release.body -replace '(?m)^\s*\*\*Full Changelog\*\*.*$', '').Trim())
    assets       = $assets
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$json = $latest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Resolve-Path (Split-Path $Out)).Path + '\' + (Split-Path $Out -Leaf), $json, (New-Object Text.UTF8Encoding $false))
Write-Host "latest.json: $($release.tag_name), $($assets.Count) file, impronte: $($sums.Count)"
