# Close applications that may be using the programmer (HI-PRO / COM port).
# Run from project root: powershell -ExecutionPolicy Bypass -File scripts/close-programmer-apps.ps1
# Options: -Close    actually close the processes (default: only list)
#          -WhatIf   only list, no close

param(
    [switch]$Close,
    [switch]$WhatIf
)

# Do not offer to close these (IDE or our app)
$excludeProcessNames = @('devenv', 'Ul8ziz.FittingApp', 'MSBuild')

# Known process names (partial match) that often use HI-PRO / programmer
$knownProgrammerProcessNames = @(
    'HIPRO', 'HI-PRO', 'Inspire', 'Noah', 'Genie', 'Connex', 'Composer',
    'SoundDesigner', 'Fitting', 'Otosuite', 'HIMSA', 'Connect', 'HIProConfig',
    'HiproConfig', 'Programmer', 'FittingModule'
)

Write-Host "========== Checking COM ports in use ==========" -ForegroundColor Cyan
$comPorts = [System.IO.Ports.SerialPort]::GetPortNames()
foreach ($port in $comPorts) {
    try {
        $sp = New-Object System.IO.Ports.SerialPort $port, 9600, None, 8, One
        $sp.Open()
        $sp.Close()
        $sp.Dispose()
        Write-Host "  $port : free (not in use)" -ForegroundColor Green
    } catch {
        $msg = $_.Exception.Message
        if ($msg -match 'access|in use|being used|denied') {
            Write-Host "  $port : IN USE by another process" -ForegroundColor Yellow
        } elseif ($msg -match 'semaphore|timeout') {
            Write-Host "  $port : may be in use or slow (e.g. Bluetooth serial)" -ForegroundColor Yellow
        } else {
            Write-Host "  $port : $msg" -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Host "========== Processes that may use the programmer ==========" -ForegroundColor Cyan
$running = Get-Process -ErrorAction SilentlyContinue
$candidates = $running | Where-Object {
    $name = $_.ProcessName
    $title = if ($_.MainWindowTitle) { $_.MainWindowTitle } else { '' }
    foreach ($e in $excludeProcessNames) { if ($name -like "*$e*") { return $false } }
    foreach ($k in $knownProgrammerProcessNames) {
        if ($name -like "*$k*" -or $title -like "*$k*") { return $true }
    }
    $false
} | Sort-Object -Property ProcessName -Unique

if (-not $candidates -or $candidates.Count -eq 0) {
    Write-Host "  No known programmer apps found running." -ForegroundColor Green
    Write-Host ""
    Write-Host "If a COM port is still in use, close the app that uses it manually (Task Manager)." -ForegroundColor Gray
    exit 0
}

foreach ($p in $candidates) {
    Write-Host "  $($p.ProcessName) (PID: $($p.Id))  $($p.MainWindowTitle)" -ForegroundColor White
}

if ($WhatIf) {
    Write-Host ""
    Write-Host "WhatIf: no processes closed. Use -Close to close them." -ForegroundColor Gray
    exit 0
}

if (-not $Close) {
    Write-Host ""
    Write-Host "To close these processes, run: powershell -ExecutionPolicy Bypass -File scripts/close-programmer-apps.ps1 -Close" -ForegroundColor Yellow
    exit 0
}

Write-Host ""
$confirm = Read-Host "Close these processes? (Y/N)"
if ($confirm -ne 'Y' -and $confirm -ne 'y') {
    Write-Host "Cancelled." -ForegroundColor Gray
    exit 0
}

foreach ($p in $candidates) {
    try {
        Stop-Process -Id $p.Id -Force -ErrorAction Stop
        Write-Host "  Closed: $($p.ProcessName) (PID: $($p.Id))" -ForegroundColor Green
    } catch {
        Write-Host "  Failed to close $($p.ProcessName): $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done. You can run the fitting app and scan for programmers." -ForegroundColor Green
