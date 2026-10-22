$j = Invoke-RestMethod -Uri 'http://127.0.0.1:45211/api/board/chart/NIFTY?interval=5minute' -TimeoutSec 120
$ist = [TimeZoneInfo]::FindSystemTimeZoneById('India Standard Time')
$today = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $ist).Date
$todayBars = @($j.candles | Where-Object {
    $t = [DateTimeOffset]::FromUnixTimeSeconds([long]$_.time)
    [TimeZoneInfo]::ConvertTime($t, $ist).Date -eq $today
})
Write-Host "IST today: $today"
Write-Host "Total candles: $($j.candles.Count)"
Write-Host "Today 5m bars: $($todayBars.Count)"
$tail = $j.candles | Select-Object -Last 6
foreach ($c in $tail) {
    $t = [DateTimeOffset]::FromUnixTimeSeconds([long]$c.time)
    $local = [TimeZoneInfo]::ConvertTime($t, $ist)
    Write-Host "$local"
}
Write-Host "Zones: B2=$($j.b2) B1=$($j.b1) S=$($j.s) S1=$($j.s1) S2=$($j.s2)"
