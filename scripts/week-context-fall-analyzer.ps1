# Multi-day context for "trap then fast fall" (NOT raw S1 cross count).
# Universe: 4 indices + equities on current board (Nifty/Bank/Fin/Sensex list).
# Uses 5m history -> ~1 week daily OHLC, your B1/S1/S2 zone math, week shape labels.
param(
    [string]$ApiBase = 'http://127.0.0.1:45211',
    [int]$ChartLoadDepth = 2500,
    [int]$Sessions = 7,
    [string]$FocusSymbol = '',
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'exports')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Get-DailyBars([string]$name) {
    $url = "$ApiBase/api/board/chart/$([uri]::EscapeDataString($name))?interval=5minute&loadDepth=$ChartLoadDepth"
    $c = Invoke-RestMethod -Uri $url -TimeoutSec 120
    $ist = [TimeZoneInfo]::FindSystemTimeZoneById('India Standard Time')
    $c.candles | ForEach-Object {
        $local = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::FromUnixTimeSeconds([int64]$_.time), $ist)
        [pscustomobject]@{ D = $local.Date; t = $local; O = [decimal]$_.open; H = [decimal]$_.high; L = [decimal]$_.low; C = [decimal]$_.close }
    } | Group-Object D | ForEach-Object {
        $b = $_.Group | Sort-Object t
        [pscustomobject]@{
            Date  = $b[0].D
            Open  = $b[0].O
            High  = ($b | Measure-Object H -Maximum).Maximum
            Low   = ($b | Measure-Object L -Minimum).Minimum
            Close = $b[-1].C
        }
    } | Sort-Object Date | Select-Object -Last $Sessions
}

function Get-Zones($y2, $y1, $today) {
  [pscustomobject]@{
    B2 = [math]::Round(($y2.High + $y1.High) / 2, 2)
    B1 = [math]::Round(($y1.High + $today.Open) / 2, 2)
    S  = [math]::Round(($y1.High + $y1.Low + $today.Open) / 3, 2)
    S1 = [math]::Round(($y1.Low + $today.Open) / 2, 2)
    S2 = [math]::Round(($y2.Low + $y1.Low) / 2, 2)
  }
}

function Get-WeekPattern($days) {
    if ($days.Count -lt 3) { return 'ThinHistory' }
    $last3 = $days | Select-Object -Last 3
    $closes = $last3.Close
    $highs = $last3.High
    if ($highs[2] -lt $highs[1] -and $highs[1] -lt $highs[0]) { return 'LowerHighs_3d' }
    if ($closes[2] -lt $closes[1] -and $closes[1] -lt $closes[0]) { return 'LowerCloses_3d' }
    if ($closes[2] -gt $closes[1] -and $closes[1] -gt $closes[0]) { return 'HigherCloses_3d' }
    'MixedWeek'
}

function Test-TrapFastFall($days) {
    if ($days.Count -lt 3) { return $null }
    $today = $days[-1]; $y1 = $days[-2]; $y2 = $days[-3]
    $z = Get-Zones $y2 $y1 $today
    $mark = if ($today.Close -ge $z.B1) { 'BUYING' } elseif ($today.Close -le $z.S1) { 'SELLING' } else { 'RANGE' }
    $highAboveB1 = $today.High -ge $z.B1
    $closeBelowS1 = $today.Close -le $z.S1
    $closeLowThird = ($today.Close - $today.Low) -le (($today.High - $today.Low) * 0.35)
    $dropFromHighPct = if ($today.High -gt 0) { [math]::Round(100.0 * ($today.High - $today.Low) / $today.High, 2) } else { 0 }
    $dayChg = if ($today.Open -gt 0) { [math]::Round(100.0 * ($today.Close - $today.Open) / $today.Open, 2) } else { 0 }
    $week = Get-WeekPattern $days
    # "LUPIN-style": week rollover or mixed + tests B1 + finishes weak (S1 or bottom of range) + violent range
    $isArchetype = $highAboveB1 -and ($closeBelowS1 -or $closeLowThird) -and $dropFromHighPct -ge 1.2 -and $dayChg -le 0
    [pscustomobject]@{
        Session            = $today.Date.ToString('yyyy-MM-dd')
        Open               = $today.Open
        High               = $today.High
        Low                = $today.Low
        Close              = $today.Close
        B1                 = $z.B1
        S1                 = $z.S1
        S2                 = $z.S2
        CloseMark          = $mark
        WeekPattern3d      = $week
        HighTestsB1        = $highAboveB1
        CloseBelowS1       = $closeBelowS1
        CloseInLowThird    = $closeLowThird
        HighToLowPct       = $dropFromHighPct
        OpenToClosePct     = $dayChg
        ArchetypeTrapFall  = $isArchetype
        PriorDaySummary    = "D-1 O=$($y1.Open) H=$($y1.High) L=$($y1.Low) C=$($y1.Close); D-2 C=$($y2.Close) H=$($y2.High)"
    }
}

$board = Invoke-RestMethod "$ApiBase/api/board" -TimeoutSec 60
$indices = @('NIFTY', 'BANKNIFTY', 'FINNIFTY', 'SENSEX')
$names = ($indices + ($board.symbols | Where-Object quoteVerified | ForEach-Object name) | Sort-Object -Unique)

$rows = [System.Collections.Generic.List[object]]::new()
$detail = [System.Collections.Generic.List[object]]::new()

foreach ($name in $names) {
    if ($FocusSymbol -and $name -ne $FocusSymbol) { continue }
    try {
        $days = Get-DailyBars $name
        if ($days.Count -lt 3) { continue }
        $r = Test-TrapFastFall $days
        if (-not $r) { continue }
        $kind = if ($indices -contains $name) { 'Index' } else { 'Stock' }
        $props = [ordered]@{ Symbol = $name; Kind = $kind }
        foreach ($p in $r.PSObject.Properties) { $props[$p.Name] = $p.Value }
        $rows.Add([pscustomobject]$props)
        if ($name -eq $FocusSymbol -or $name -eq 'LUPIN') {
            foreach ($d in ($days | Select-Object -Last 5)) { $detail.Add([pscustomobject]@{ Symbol = $name; Day = $d }) }
        }
    }
    catch { }
    Start-Sleep -Milliseconds 80
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$path = Join-Path $OutDir "week-trap-fall-latest-$stamp.csv"
$rows | Export-Csv $path -NoTypeInformation

$arch = $rows | Where-Object ArchetypeTrapFall -eq $true | Sort-Object Kind, Symbol
Write-Host ''
Write-Host "=== Latest session: week context + trap/fast-fall (listed board + 4 indices) ===" -ForegroundColor Cyan
Write-Host "Symbols scanned: $($names.Count)  Archetype matches: $($arch.Count)  CSV: $path"
$arch | Select-Object Symbol, Kind, WeekPattern3d, Open, High, Low, Close, B1, S1, HighToLowPct, OpenToClosePct, CloseMark | Format-Table -AutoSize

$near = $rows | Where-Object { $_.HighTestsB1 -and $_.OpenToClosePct -lt 0 } | Sort-Object { [decimal]$_.HighToLowPct } -Descending
if ($near.Count -gt 0) {
    Write-Host '--- Near-miss: tested B1, red day (week context) ---' -ForegroundColor DarkYellow
    $near | Select-Object -First 20 Symbol, Kind, WeekPattern3d, Session, HighToLowPct, OpenToClosePct, CloseMark, ArchetypeTrapFall | Format-Table -AutoSize
}

if ($FocusSymbol -eq 'LUPIN' -or -not $FocusSymbol) {
    Write-Host '--- LUPIN last 5 daily bars ---' -ForegroundColor Yellow
    $lu = $rows | Where-Object Symbol -eq 'LUPIN'
    if ($lu) { $lu | Format-List WeekPattern3d, PriorDaySummary, HighTestsB1, CloseBelowS1, ArchetypeTrapFall }
}
