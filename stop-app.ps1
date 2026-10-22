$ErrorActionPreference = 'SilentlyContinue'

function Stop-ListenersOnPort([int]$Port) {
    for ($i = 0; $i -lt 5; $i++) {
        $connections = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
        if ($connections.Count -eq 0) {
            return
        }

        foreach ($c in $connections) {
            $procId = $c.OwningProcess
            if ($procId -gt 0) {
                Write-Host "Stopping PID $procId on port $Port"
                Stop-Process -Id $procId -Force
            }
        }

        Start-Sleep -Seconds 1
    }
}

Stop-ListenersOnPort 45211
Stop-ListenersOnPort 45212
Start-Sleep -Seconds 2
Write-Host 'Ports 45211 and 45212 cleared.'
