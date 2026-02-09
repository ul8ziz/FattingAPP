# Manual check of COM and USB ports (no app required).
# Run from project root: powershell -ExecutionPolicy Bypass -File scripts/check-com-usb.ps1

Write-Host "========== COM PORTS (as seen by .NET / app) ==========" -ForegroundColor Cyan
try {
    $ports = [System.IO.Ports.SerialPort]::GetPortNames()
    if ($ports.Count -eq 0) {
        Write-Host "  No COM ports found."
    } else {
        $ports | Sort-Object { [int]($_ -replace 'COM', '') } | ForEach-Object { Write-Host "  $_" }
    }
} catch {
    Write-Host "  Error: $_"
}

Write-Host ""
Write-Host "========== COM / Serial / HI-PRO devices (WMI) ==========" -ForegroundColor Cyan
Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match 'COM\d+|Serial|HI-PRO|FTDI|USB.*Serial' } |
    ForEach-Object { Write-Host "  $($_.Name)  [$($_.Status)]" }

Write-Host ""
Write-Host "========== USB devices (summary) ==========" -ForegroundColor Cyan
Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction SilentlyContinue |
    Where-Object { $_.DeviceID -match '^USB\\' -and $_.Name -notmatch 'Root Hub|Host Controller' } |
    Select-Object -ExpandProperty Name |
    ForEach-Object { Write-Host "  $_" }

Write-Host ""
Write-Host "========== HI-PRO specific ==========" -ForegroundColor Cyan
$hipro = Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match 'HI-PRO' }
if ($hipro) {
    $hipro | ForEach-Object { Write-Host "  $($_.Name)  Status: $($_.Status)  DeviceID: $($_.DeviceID)" }
} else {
    Write-Host "  No HI-PRO device found."
}

Write-Host ""
Write-Host "Done. Use these results to verify COM/USB before running the app." -ForegroundColor Green
