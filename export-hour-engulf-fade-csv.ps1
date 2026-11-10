# Hour-engulf fade backtest CSV (needs backend :45211 + m.Stock history).
param(
    [int]$LookbackDays = 120,
    [string]$ApiBase = 'http://127.0.0.1:45211',
    [string]$OutDir = (Join-Path $PSScriptRoot 'exports'),
    [bool]$ApplyFilters = $true
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd'
$filt = if ($ApplyFilters) { 'filtered' } else { 'all' }

foreach ($mode in @('FADE_LONG_STOCK', 'FADE_LONG_INDEX_FUTURE', 'SHORT_GAP_STOCK')) {
    $url = "$ApiBase/api/paper-agent/backtest/hour-engulf-fade-pnl/trades.csv?lookbackDays=$LookbackDays&mode=$mode&applyFilters=$ApplyFilters"
    $out = Join-Path $OutDir "hour-engulf-$mode-${filt}-${LookbackDays}d-$stamp.csv"
    Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 900 -OutFile $out
    Write-Host "Saved $out"
}

$summary = Invoke-RestMethod -Uri "$ApiBase/api/paper-agent/backtest/hour-engulf-fade-pnl?lookbackDays=$LookbackDays&applyFilters=$ApplyFilters" -TimeoutSec 900
$summary | ConvertTo-Json -Depth 4 | Out-File (Join-Path $OutDir "hour-engulf-summary-${LookbackDays}d-$stamp.json")
Write-Host "Summary win rate $($summary.winRatePercent)% total INR $($summary.totalInrPnl) expectancy $($summary.expectancyInrPerTrade) per trade"
