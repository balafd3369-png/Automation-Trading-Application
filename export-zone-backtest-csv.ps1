# Downloads zone-board backtest trade journals as CSV (requires backend on 45211 + broker session).
param(
    [int]$LookbackDays = 90,
    [string]$ApiBase = 'http://127.0.0.1:45211',
    [string]$OutDir = (Join-Path $PSScriptRoot 'exports')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Save-CsvFromApi([string]$Url, [string]$OutPath) {
    Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 600 -OutFile $OutPath
    Write-Host "Saved $OutPath"
}

$stamp = Get-Date -Format 'yyyyMMdd'
Save-CsvFromApi `
    "$ApiBase/api/paper-agent/backtest/zone-board/trades-all-indices.csv?lookbackDays=$LookbackDays" `
    (Join-Path $OutDir "zone-backtest-ALL-INDICES-${LookbackDays}d-$stamp.csv")

foreach ($sym in @('NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX')) {
    Save-CsvFromApi `
        "$ApiBase/api/paper-agent/backtest/zone-board/$sym/trades.csv?lookbackDays=$LookbackDays" `
        (Join-Path $OutDir "zone-backtest-$sym-${LookbackDays}d-$stamp.csv")
}

Write-Host "Done. Open CSV files in: $OutDir"
