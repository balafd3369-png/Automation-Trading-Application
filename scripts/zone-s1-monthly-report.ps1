# ZONE_S1_BREAK_S2 — monthly frequency + win rate across board symbols (stocks + indices).
# Requires backend on 45211 with broker chart history for backtest.
param(
    [int]$LookbackDays = 120,
    [string]$ApiBase = 'http://127.0.0.1:45211',
    [string]$Pattern = 'ZONE_S1_BREAK_S2',
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'exports')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$indices = @('NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX')
$board = Invoke-RestMethod -Uri "$ApiBase/api/board" -TimeoutSec 60
$stockNames = $board.symbols |
    Where-Object { $_.quoteVerified -and ($indices -notcontains $_.name) } |
    ForEach-Object { $_.name } |
    Sort-Object -Unique
$allSymbols = ($indices + $stockNames) | Sort-Object -Unique

$allTrades = [System.Collections.Generic.List[object]]::new()
$perSymbol = [System.Collections.Generic.List[object]]::new()
$fail = [System.Collections.Generic.List[string]]::new()

foreach ($sym in $allSymbols) {
    $url = "$ApiBase/api/paper-agent/backtest/zone-board/$([uri]::EscapeDataString($sym))/trades?lookbackDays=$LookbackDays&pattern=$Pattern"
    try {
        $j = Invoke-RestMethod -Uri $url -TimeoutSec 180
        $trades = @($j.trades)
        if ($trades.Count -eq 0) {
            continue
        }
        $sessionGroups = $trades | Group-Object sessionDate
        $n = $sessionGroups.Count
        $wins = 0
        $losses = 0
        foreach ($g in $sessionGroups) {
            if (@($g.Group | Where-Object { $_.result -eq 'WIN' }).Count -gt 0) { $wins++ }
            else { $losses++ }
        }
        $wr = if ($n -gt 0) { [math]::Round(100.0 * $wins / $n, 2) } else { 0 }
        $kind = if ($indices -contains $sym) { 'Index' } else { 'Stock' }
        $perSymbol.Add([pscustomobject]@{
            Symbol       = $sym
            Kind         = $kind
            Trades       = $n
            Wins         = $wins
            Losses       = $losses
            WinRatePct   = $wr
            Qualifies55  = ($n -ge 8 -and $wr -ge 55)
        })
        # First signal per symbol per session (dedupe repeated 5m S1 crosses same day).
        $bySession = $trades | Group-Object { "$sym|$($_.sessionDate)" }
        foreach ($g in $bySession) {
            $t = $g.Group | Select-Object -First 1
            $ym = if ($t.sessionDate) { $t.sessionDate.ToString().Substring(0, 7) } else { 'unknown' }
            $dayWins = @($g.Group | Where-Object { $_.result -eq 'WIN' }).Count
            $dayResult = if ($dayWins -gt 0) { 'WIN' } else { ($g.Group | Select-Object -Last 1).result }
            $allTrades.Add([pscustomobject]@{
                YearMonth  = $ym
                Symbol     = $sym
                Kind       = $kind
                Result     = $dayResult
                PointsPnl  = $t.pointsPnl
                Session    = $t.sessionDate
                RawCount   = $g.Count
            })
        }
    }
    catch {
        $fail.Add($sym)
    }
    Start-Sleep -Milliseconds 120
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$perSymbolPath = Join-Path $OutDir "zone-s1-per-symbol-${LookbackDays}d-$stamp.csv"
$perSymbol | Export-Csv -Path $perSymbolPath -NoTypeInformation

$monthly = $allTrades |
    Group-Object YearMonth |
    ForEach-Object {
        $rows = $_.Group
        $w = @($rows | Where-Object { $_.Result -eq 'WIN' }).Count
        $n = $rows.Count
        $st = @($rows | Where-Object { $_.Kind -eq 'Stock' }).Count
        $ix = @($rows | Where-Object { $_.Kind -eq 'Index' }).Count
        [pscustomobject]@{
            YearMonth           = $_.Name
            TotalSignals        = $n
            StockSignals        = $st
            IndexSignals        = $ix
            Wins                = $w
            Losses              = $n - $w
            WinRatePct          = if ($n -gt 0) { [math]::Round(100.0 * $w / $n, 2) } else { 0 }
            UniqueSymbols       = (@($rows | Select-Object -ExpandProperty Symbol -Unique)).Count
            AvgSignalsPerDayEst = if ($n -gt 0) { [math]::Round($n / 22.0, 2) } else { 0 }
            Note                = 'One row per symbol per session day (deduped)'
        }
    } |
    Sort-Object YearMonth

$monthlyPath = Join-Path $OutDir "zone-s1-monthly-${LookbackDays}d-$stamp.csv"
$monthly | Export-Csv -Path $monthlyPath -NoTypeInformation

$totN = $allTrades.Count
$totW = @($allTrades | Where-Object { $_.Result -eq 'WIN' }).Count
$months = @($monthly | Where-Object { $_.YearMonth -ne 'unknown' }).Count
$avgPerMonth = if ($months -gt 0) { [math]::Round($totN / $months, 1) } else { 0 }

Write-Host ''
Write-Host "=== ZONE_S1_BREAK_S2  (lookback $LookbackDays calendar days of data)" -ForegroundColor Cyan
Write-Host "Symbols scanned: $($allSymbols.Count)  Failed: $($fail.Count)"
Write-Host "Total signals: $totN  Wins: $totW  Overall WR: $([math]::Round(100.0 * $totW / [math]::Max(1,$totN),2))%"
Write-Host "Calendar months with data: $months  Avg signals per month (universe): $avgPerMonth"
Write-Host ''
Write-Host '--- Per calendar month ---' -ForegroundColor Yellow
$monthly | Format-Table YearMonth, TotalSignals, StockSignals, IndexSignals, WinRatePct, UniqueSymbols -AutoSize
Write-Host "Per-symbol CSV: $perSymbolPath"
Write-Host "Monthly CSV:    $monthlyPath"
