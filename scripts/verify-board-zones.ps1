$ErrorActionPreference = 'Stop'
$board = Invoke-RestMethod -Uri 'http://127.0.0.1:45211/api/board' -TimeoutSec 60

function Round-Line($v) { [Math]::Round([decimal]$v, 2, [MidpointRounding]::AwayFromZero) }

function Expected-Zones($row) {
    $o = [decimal]$row.todayOpen
    $yH = [decimal]$row.yesterdayHigh
    $yL = [decimal]$row.yesterdayLow
    $h2 = [decimal]$row.highTwoDaysAgo
    $l2 = [decimal]$row.lowTwoDaysAgo
    @{
        b2 = Round-Line(($h2 + $yH) / 2)
        b1 = Round-Line(($yH + $o) / 2)
        s  = Round-Line(($yH + $yL + $o) / 3)
        s1 = Round-Line(($yL + $o) / 2)
        s2 = Round-Line(($l2 + $yL) / 2)
    }
}

$bad = @()
$skipped = @()
$ok = 0

foreach ($sym in $board.symbols) {
    if ($sym.todayOpen -le 0 -or $sym.yesterdayHigh -le 0 -or $sym.yesterdayLow -le 0) {
        $skipped += $sym.name
        continue
    }
    if ($sym.highTwoDaysAgo -le 0 -or $sym.lowTwoDaysAgo -le 0) {
        $skipped += $sym.name
        continue
    }
    $exp = Expected-Zones $sym
    $fields = @('b2','b1','s','s1','s2')
    $mismatch = $false
    $detail = @()
    foreach ($f in $fields) {
        $got = Round-Line($sym.$f)
        $want = $exp[$f]
        if ($got -ne $want) {
            $mismatch = $true
            $detail += "$f got=$got want=$want"
        }
    }
    if ($mismatch) {
        $bad += [pscustomobject]@{ name = $sym.name; detail = ($detail -join '; ') }
    } else {
        $ok++
    }
}

Write-Host "Board symbols: $($board.symbols.Count)"
Write-Host "Zones OK (formula match): $ok"
Write-Host "Skipped (missing OHLC): $($skipped.Count)"
if ($skipped.Count -gt 0 -and $skipped.Count -le 15) {
    Write-Host "  Skipped: $($skipped -join ', ')"
}
Write-Host "MISMATCHES: $($bad.Count)"
if ($bad.Count -gt 0) {
    $bad | Format-Table -AutoSize
}

# Sample indices
Write-Host "`n--- Indices (spot check) ---"
foreach ($n in @('NIFTY','BANKNIFTY','FINNIFTY','SENSEX')) {
    $s = $board.symbols | Where-Object { $_.name -eq $n } | Select-Object -First 1
    if (-not $s) { continue }
    $e = Expected-Zones $s
    Write-Host "$n  B2=$($s.b2) B1=$($s.b1) S=$($s.s) S1=$($s.s1) S2=$($s.s2)  | formula B2=$($e.b2) ... S2=$($e.s2)"
}

exit $(if ($bad.Count -gt 0) { 1 } else { 0 })
