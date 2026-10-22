$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$backend = Join-Path $root 'backend'
$frontend = Join-Path $root 'frontend'

Write-Host 'Stopping any old backend/frontend on 45211 and 45212...' -ForegroundColor Yellow
& (Join-Path $root 'stop-app.ps1')

Write-Host 'Building backend...' -ForegroundColor Cyan
Push-Location $backend
dotnet build -v q
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Host 'Backend build failed.' -ForegroundColor Red
    exit 1
}
Pop-Location

Write-Host 'Starting backend (45211)...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit',
    '-Command',
    "Set-Location '$backend'; `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --no-build --no-launch-profile --urls http://0.0.0.0:45211"
)

$deadline = (Get-Date).AddSeconds(45)
$backendUp = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    foreach ($url in @('http://127.0.0.1:45211/api/health', 'http://127.0.0.1:45211/api/board')) {
        try {
            $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) {
                $backendUp = $true
                break
            }
        }
        catch { }
    }
    if ($backendUp) { break }
}

if (-not $backendUp) {
    Write-Host 'Backend did not respond on /api/health within 45s. Check the backend window for errors.' -ForegroundColor Red
    exit 1
}

Write-Host 'Backend is up.' -ForegroundColor Green

Write-Host 'Starting frontend (45212)...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit',
    '-Command',
    "Set-Location '$frontend'; npm start"
)

Start-Sleep -Seconds 12
try {
    $ui = Invoke-WebRequest -Uri 'http://127.0.0.1:45212' -UseBasicParsing -TimeoutSec 8
    Write-Host "Frontend is up (HTTP $($ui.StatusCode))." -ForegroundColor Green
}
catch {
    Write-Host 'Frontend still starting — open http://127.0.0.1:45212 in ~30s.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Open: http://127.0.0.1:45212' -ForegroundColor Green
Write-Host 'API:  http://127.0.0.1:45211/api/board' -ForegroundColor Green
Write-Host 'Keep both PowerShell windows open while trading.' -ForegroundColor DarkGray
