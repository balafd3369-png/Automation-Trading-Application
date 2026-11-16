# Upload release artifacts and deploy scripts to a droplet (does not run deploy-on-droplet).
param(
    [Parameter(Mandatory = $true)]
    [string] $Server,
    [string] $User = 'root',
    [string] $IdentityFile = '',
    [string] $RemoteDir = '/tmp/twb-release'
)

$ErrorActionPreference = 'Stop'
$DeployDir = $PSScriptRoot
$Artifacts = Join-Path $DeployDir 'artifacts'
$Bundle = Join-Path $Artifacts 'bundle'

if (-not (Test-Path (Join-Path $Artifacts 'api\TradeWithBeast.dll'))) {
    throw "Missing API artifacts. Run deploy-release.ps1 first."
}
if (-not (Test-Path (Join-Path $Bundle 'deploy-on-droplet.sh'))) {
    throw "Missing bundle scripts. Run deploy-release.ps1 first."
}

$sshArgs = @()
if ($IdentityFile) { $sshArgs += @('-i', $IdentityFile) }

$remote = "${User}@${Server}"
Write-Host "Uploading to ${remote}:${RemoteDir} ..."
ssh @sshArgs $remote "mkdir -p '${RemoteDir}/artifacts' '${RemoteDir}/bundle'"
scp @sshArgs -r (Join-Path $Artifacts 'api') "${remote}:${RemoteDir}/artifacts/"
scp @sshArgs -r (Join-Path $Artifacts 'web') "${remote}:${RemoteDir}/artifacts/"
scp @sshArgs -r "$Bundle/*" "${remote}:${RemoteDir}/"
Write-Host "Uploaded. On server run:"
Write-Host "  cd ${RemoteDir} && bash deploy-on-droplet.sh ./artifacts/api ./artifacts/web"