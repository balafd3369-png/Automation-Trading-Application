# Build release artifacts on a developer machine (not on the droplet).
# Upload bundle via upload-release.ps1 after preflight passes.

$ErrorActionPreference = 'Stop'
$Root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Backend = Join-Path $Root 'backend'
$Frontend = Join-Path $Root 'frontend'
$DeployDir = Join-Path $Root 'deploy\digitalocean'
$Out = Join-Path $DeployDir 'artifacts'
$Bundle = Join-Path $Out 'bundle'

function Get-FileSha256Hex($Path) {
    $hash = Get-FileHash -Path $Path -Algorithm SHA256
    return $hash.Hash.ToLowerInvariant()
}

New-Item -ItemType Directory -Force -Path $Out, (Join-Path $Out 'api'), (Join-Path $Out 'web'), $Bundle | Out-Null

Push-Location $Backend
dotnet restore TradeWithBeast.csproj
dotnet build TradeWithBeast.csproj -c Release --no-restore
dotnet publish TradeWithBeast.csproj -c Release -o (Join-Path $Out 'api') --no-build
Pop-Location

$rcPath = Join-Path $Out 'api\TradeWithBeast.runtimeconfig.json'
if (-not (Test-Path $rcPath)) { throw "Missing runtimeconfig after publish: $rcPath" }
$rc = Get-Content $rcPath -Raw | ConvertFrom-Json
if ($rc.runtimeOptions.tfm -ne 'net10.0') {
    throw "Backend must target net10.0; got $($rc.runtimeOptions.tfm)"
}

Push-Location $Frontend
npm install
npm run build -- --configuration=production
Copy-Item -Recurse -Force (Join-Path $Frontend 'dist\frontend\*') (Join-Path $Out 'web')
Pop-Location

$deployFiles = @(
    'deploy-on-droplet.sh',
    'backup-sqlite.sh',
    'rollback.sh',
    'verify-infrastructure.sh',
    'provision-persistent-volume.sh',
    'env.template',
    'nginx-tradewithbeast.conf',
    'tradewithbeast-api.service'
)
foreach ($name in $deployFiles) {
    $src = Join-Path $DeployDir $name
    if (-not (Test-Path $src)) { throw "Missing deploy file: $src" }
    Copy-Item -Force $src (Join-Path $Bundle $name)
}

$gitCommit = ''
$gitBranch = ''
if (Get-Command git -ErrorAction SilentlyContinue) {
    Push-Location $Root
    $gitCommit = (git rev-parse HEAD 2>$null)
    $gitBranch = (git rev-parse --abbrev-ref HEAD 2>$null)
    Pop-Location
}

$dllPath = Join-Path $Out 'api\TradeWithBeast.dll'
$indexPath = Join-Path $Out 'web\index.html'
$manifest = [ordered]@{
    backend      = 'net10.0 Release'
    gitCommit    = $gitCommit
    gitBranch    = $gitBranch
    builtAtUtc   = (Get-Date).ToUniversalTime().ToString('o')
    frontend     = 'production'
    liveTradingEnabled = $false
    sqlitePackages = [ordered]@{
        SQLitePCLRaw_bundle_e_sqlite3 = '3.0.5'
        SQLitePCLRaw_lib_e_sqlite3    = '3.50.3'
        Microsoft_Data_Sqlite         = '10.0.0'
    }
    checksums    = [ordered]@{
        TradeWithBeastDll = (Get-FileSha256Hex $dllPath)
        webIndexHtml      = (Get-FileSha256Hex $indexPath)
    }
}
$manifestPath = Join-Path $Out 'build-manifest.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8
Copy-Item -Force $manifestPath (Join-Path $Bundle 'build-manifest.json')

Write-Host "Artifacts ready: $Out"
Write-Host "Bundle (scripts + manifest): $Bundle"
Write-Host "Next: bash deploy/digitalocean/preflight.sh then upload-release.ps1"