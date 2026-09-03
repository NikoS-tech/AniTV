$ErrorActionPreference = 'Stop'
$aniPublishDirectory = Join-Path $PSScriptRoot 'publish'
$aniExecutable = Join-Path $aniPublishDirectory 'AniTV.exe'
$aniRunning = Get-Process AniTV -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $aniExecutable }
if ($aniRunning) {
    throw 'AniTV ещё запущен. Закройте приложение перед обновлением. Единственная папка сборки — publish; альтернативная копия не создаётся.'
}
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:APPDATA = Join-Path $PSScriptRoot '.appdata'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore (Join-Path $PSScriptRoot 'AniTV.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish (Join-Path $PSScriptRoot 'AniTV.csproj') -c Release --self-contained false --no-restore -o $aniPublishDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "`nBuilt: publish\AniTV.exe"
