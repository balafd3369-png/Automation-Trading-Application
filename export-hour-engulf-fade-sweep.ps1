# Full universe sweep for 1h engulf fade P&L (backend :45211 + m.Stock 5m history).
param(
    [int]$LookbackDays = 120,
    [decimal]$StockNotionalInr = 50000,
    [string]$ApiBase = 'http://127.0.0.1:45211',
    [string]$OutDir = (Join-Path $PSScriptRoot 'exports')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd'
$url = "$ApiBase/api/paper-agent/backtest/hour-engulf-fade-pnl/sweep?lookbackDays=$LookbackDays&stockNotionalInr=$StockNotionalInr"
$out = Join-Path $OutDir "hour-engulf-sweep-${LookbackDays}d-notional${StockNotionalInr}-$stamp.json"
Invoke-RestMethod -Uri $url -TimeoutSec 3600 | ConvertTo-Json -Depth 6 | Out-File $out -Encoding utf8
Write-Host "Saved $out"
Get-Content $out | Select-Object -First 40
