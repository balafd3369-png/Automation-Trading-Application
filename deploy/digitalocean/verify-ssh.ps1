#Requires -Version 5.1
<#
.SYNOPSIS
  Verifies SSH readiness to an explicitly supplied host. Does not connect without -Server.

.PARAMETER Server
  Droplet public IPv4 or DNS hostname (required).

.PARAMETER User
  SSH login user (default: root).

.PARAMETER Port
  SSH port (default: 22).

.PARAMETER IdentityFile
  Optional path to private key file. Never read or display key contents.

.PARAMETER ConnectTimeoutSeconds
  TCP and SSH timeout (default: 15).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [Parameter(Mandatory = $false)]
    [string]$User = 'root',

    [Parameter(Mandatory = $false)]
    [int]$Port = 22,

    [Parameter(Mandatory = $false)]
    [string]$IdentityFile = '',

    [Parameter(Mandatory = $false)]
    [int]$ConnectTimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'

function Write-Result {
    param([string]$Level, [string]$Message)
    Write-Host "$Level  $Message"
}

if ([string]::IsNullOrWhiteSpace($Server)) {
    Write-Result 'FAIL' 'Server parameter is required.'
    exit 1
}

# Reject obvious placeholders
if ($Server -match '^(yourdomain|example\.com|DROPLET_IP|<.*>)$') {
    Write-Result 'FAIL' "Server looks like a placeholder: $Server"
    exit 1
}

Write-Result 'INFO' "Target: ${User}@${Server}:${Port}"

# TCP port check
try {
    $client = New-Object System.Net.Sockets.TcpClient
    $async = $client.BeginConnect($Server, $Port, $null, $null)
    $wait = $async.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds($ConnectTimeoutSeconds))
    if (-not $wait) {
        Write-Result 'FAIL' "TCP connection timed out on port $Port (firewall, wrong IP, or SSH not listening)."
        exit 1
    }
    $client.EndConnect($async)
    $client.Close()
    Write-Result 'PASS' "TCP port $Port reachable on $Server"
}
catch {
    Write-Result 'FAIL' "TCP connection failed: $($_.Exception.Message)"
    exit 1
}

# Build ssh command (host key verification enabled — no StrictHostKeyChecking=no)
$sshArgs = @(
    '-o', 'BatchMode=yes',
    '-o', "ConnectTimeout=$ConnectTimeoutSeconds",
    '-o', 'PasswordAuthentication=no',
    '-p', $Port,
    "${User}@${Server}",
    'echo twb-ssh-ok'
)

if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) {
    if (-not (Test-Path -LiteralPath $IdentityFile)) {
        Write-Result 'FAIL' "Identity file not found: $IdentityFile"
        exit 1
    }
    $sshArgs = @('-i', $IdentityFile) + $sshArgs
}

$ssh = Get-Command ssh -ErrorAction SilentlyContinue
if (-not $ssh) {
    Write-Result 'FAIL' 'OpenSSH client (ssh.exe) not found in PATH.'
    exit 1
}

try {
    $output = & ssh @sshArgs 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        $detail = ($output | Out-String).Trim()
        if ($detail -match 'Permission denied') {
            Write-Result 'FAIL' 'SSH authentication failed (Permission denied). Add your public key to the droplet or specify -IdentityFile.'
        }
        elseif ($detail -match 'Host key verification failed') {
            Write-Result 'FAIL' 'Host key verification failed. Run ssh once interactively to accept the host key, then retry.'
        }
        else {
            Write-Result 'FAIL' "SSH command failed (exit $code). $detail"
        }
        exit 1
    }
    Write-Result 'PASS' 'SSH authentication succeeded (remote echo twb-ssh-ok).'
    exit 0
}
catch {
    Write-Result 'FAIL' "SSH invocation error: $($_.Exception.Message)"
    exit 1
}