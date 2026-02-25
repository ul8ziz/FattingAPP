# check-comlock.ps1 - Check COM port availability and whether COM2 is locked by another process/service.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\check-comlock.ps1

$ErrorActionPreference = "Continue"
$report = @()

$report += "=== check-comlock.ps1 run at $(Get-Date -Format 'o') ==="
$report += ""

# 1) List COM ports
$report += "--- COM ports (SerialPort.GetPortNames) ---"
try {
    $ports = [System.IO.Ports.SerialPort]::GetPortNames()
    $report += "Ports: " + ($ports -join ", ")
    if (-not $ports) { $report += "(none)" }
} catch {
    $report += "Error: $_"
}
$report += ""

# 2) Attempt to open COM2
$report += "--- COM2 open attempt ---"
$com2Free = $null
try {
    $sp = New-Object System.IO.Ports.SerialPort "COM2"
    $sp.Open()
    $sp.Close()
    $sp.Dispose()
    $com2Free = $true
    $report += "COM2: OPEN succeeded (port is free)"
} catch {
    $com2Free = $false
    $report += "COM2: OPEN failed - $($_.Exception.Message)"
    if ($_.Exception.Message -match "access denied|in use|being used") {
        $report += "COM2 appears LOCKED (access denied or in use)"
    }
}
$report += ""

# 3) Suspected locking processes
$report += "--- Processes that may hold HI-PRO/CTK/COM ---"
try {
    $procs = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -match "hi|hpro|ctk|signa|klara|sd|monitor|service|fitting|inspire|starkey|noah"
    }
    if ($procs) {
        $procs | ForEach-Object { $report += "  $($_.ProcessName) (Id=$($_.Id))" }
    } else {
        $report += "  (none matching)"
    }
} catch {
    $report += "  Error: $_"
}
$report += ""

# 4) Related services
$report += "--- Services (HiPro|CTK|Signa|Klara|hi_) ---"
try {
    $svc = Get-Service -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -match "HiPro|CTK|Signa|Klara|hi_|FTSER|ftdibus"
    }
    if ($svc) {
        $svc | ForEach-Object { $report += "  $($_.Name) Status=$($_.Status)" }
    } else {
        $report += "  (none matching)"
    }
} catch {
    $report += "  Error: $_"
}
$report += ""

# Summary
$report += "--- Summary ---"
if ($com2Free -eq $true) {
    $report += "COM2 status: FREE (open succeeded)"
} elseif ($com2Free -eq $false) {
    $report += "COM2 status: LOCKED or inaccessible (open failed). Check processes and services above."
} else {
    $report += "COM2 status: unknown (no COM2 in list or error)"
}
$report += "=== end check-comlock ==="
$report -join "`r`n" | Write-Output
