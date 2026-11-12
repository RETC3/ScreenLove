# Crea l'eseguibile autonomo di ScreenLove (include .NET: nessuna installazione richiesta).
#   .\scripts\publish.ps1            -> dist\win-x64\ScreenLove.exe
#   .\scripts\publish.ps1 -Arm64     -> anche dist\win-arm64\ScreenLove.exe
#   .\scripts\publish.ps1 -Zip       -> crea anche dist\ScreenLove-<versione>-<rid>.zip
param(
    [switch]$Arm64,
    [switch]$Zip
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$project = "$root\src\ScreenLove\ScreenLove.csproj"

# dotnet dal PATH, altrimenti l'installazione per utente
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "SDK .NET 8 non trovato: installalo da https://dotnet.microsoft.com/download/dotnet/8.0" }

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$profiles = @('win-x64')
if ($Arm64) { $profiles += 'win-arm64' }

foreach ($p in $profiles) {
    Write-Host "== Pubblicazione $p ==" -ForegroundColor Magenta
    & $dotnet publish $project "-p:PublishProfile=$p" -nologo
    if ($LASTEXITCODE -ne 0) { throw "Pubblicazione $p non riuscita" }

    $exe = Get-Item "$root\dist\$p\ScreenLove.exe"
    Write-Host ("   {0}  ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))

    if ($Zip) {
        $zipPath = "$root\dist\ScreenLove-$version-$p.zip"
        Compress-Archive -Path $exe.FullName -DestinationPath $zipPath -Force
        Write-Host "   $zipPath"
    }
}
