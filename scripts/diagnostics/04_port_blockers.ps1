# 04_port_blockers.ps1 - Detect port locks and blockers (Inspire|Starkey|Updater|HiPro|Monitor).
# Input: -PortsJson <path>. Optional -AutoStopStarkeyInspire (elevated only). Output: logs\diagnostics\blockers.json

param(
    [string]$PortsJson = $null,   # Path to ports.json file
    [string]$LogsDiagnosticsDir = $null,
    [switch]$AutoStopStarkeyInspire = $false,
    [switch]$JsonOnly = $false
)

$ErrorActionPreference = 'Continue'

# Blocker patterns: Inspire|Starkey|Updater|HiPro|Monitor (for listing). For STOP: Starkey|Inspire only.
$processPattern = 'Inspire|Starkey|Updater|HiPro|Monitor'
$stopNamePattern = 'Starkey|Inspire'

# --- Port list from file ---
$portList = @()
if ($PortsJson -and (Test-Path -LiteralPath $PortsJson -PathType Leaf)) {
    try {
        $raw = Get-Content -LiteralPath $PortsJson -Raw -Encoding UTF8
        $decoded = $raw | ConvertFrom-Json
        if ($decoded -is [array]) {
            $portList = $decoded | Where-Object { $_.PortName -match '^COM\d+$' } | Select-Object -ExpandProperty PortName -Unique
        } elseif ($decoded.value -is [array]) {
            $portList = $decoded.value | Where-Object { $_.PortName -match '^COM\d+$' } | Select-Object -ExpandProperty PortName -Unique
        } else {
            if ($decoded.PortName) { $portList = @($decoded.PortName) }
        }
    } catch { }
}
if ($portList.Count -eq 0) {
    try {
        $portList = [System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object { [int]($_ -replace 'COM', '') }
    } catch {
        $portList = @()
    }
}

# --- Port lock test ---
$portLockResults = [System.Collections.ArrayList]::new()
function Test-PortLock {
    param([string]$PortName)
    try {
        $parity = [System.IO.Ports.Parity]::None
        $stopBits = [System.IO.Ports.StopBits]::One
        $port = New-Object System.IO.Ports.SerialPort($PortName, 9600, $parity, 8, $stopBits)
        $port.ReadTimeout = 200
        $port.WriteTimeout = 200
        $port.Open()
        $port.Close()
        $port.Dispose()
        return @{ PortName = $PortName; Locked = $false; Exception = $null }
    } catch {
        $ex = $_.Exception.Message
        $locked = $ex -match 'Access is denied|being used|in use|denied'
        return @{ PortName = $PortName; Locked = $locked; Exception = $ex }
    }
}
foreach ($p in $portList) {
    $r = Test-PortLock -PortName $p
    [void]$portLockResults.Add([PSCustomObject]$r)
}

# --- Processes matching blocker pattern ---
$blockerProcesses = [System.Collections.ArrayList]::new()
try {
    Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -match $processPattern
    } | ForEach-Object {
        $path = $_.ExecutablePath
        if (-not $path -and $_.CommandLine) { $path = $_.CommandLine }
        [void]$blockerProcesses.Add([PSCustomObject]@{
            ProcessName = $_.Name
            Id          = $_.ProcessId
            Path        = $path
        })
    }
} catch { }

# --- Services matching blocker pattern ---
$blockerServices = [System.Collections.ArrayList]::new()
try {
    Get-CimInstance -ClassName Win32_Service -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -match $processPattern -or $_.DisplayName -match $processPattern
    } | ForEach-Object {
        [void]$blockerServices.Add([PSCustomObject]@{
            ServiceName = $_.Name
            DisplayName = $_.DisplayName
            Status      = $_.State
            StartType   = $_.StartMode
        })
    }
} catch { }

# --- Optional: Auto-stop (only when elevated and -AutoStopStarkeyInspire) ---
$stoppedProcesses = @()
$stoppedServices = @()
$stillRunning = @()
$stillServices = @()

$isAdmin = $false
try {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} catch { }

if ($AutoStopStarkeyInspire -and $isAdmin) {
    # Stop services that contain Starkey or Inspire (case-insensitive)
    foreach ($svc in $blockerServices) {
        if ($svc.ServiceName -notmatch $stopNamePattern -and $svc.DisplayName -notmatch $stopNamePattern) { continue }
        try {
            $s = Get-Service -Name $svc.ServiceName -ErrorAction Stop
            if ($s.Status -eq 'Running') {
                Stop-Service -Name $svc.ServiceName -Force -ErrorAction Stop
                $stoppedServices += $svc.ServiceName
            } else {
                $stillServices += $svc.ServiceName
            }
        } catch {
            $stillServices += $svc.ServiceName
        }
    }
    # Stop processes that contain Starkey or Inspire
    foreach ($proc in $blockerProcesses) {
        if ($proc.ProcessName -notmatch $stopNamePattern) { continue }
        try {
            $p = Get-Process -Id $proc.Id -ErrorAction Stop
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            $stoppedProcesses += "$($proc.ProcessName) (PID $($proc.Id))"
        } catch {
            $stillRunning += "$($proc.ProcessName) (PID $($proc.Id))"
        }
    }
} elseif ($AutoStopStarkeyInspire -and -not $isAdmin) {
    $stillRunning = @("Not elevated; cannot stop services.")
    $stillServices = @("Not elevated; cannot stop services.")
}

# Write blockers.json
$repoRoot = Join-Path $PSScriptRoot '..\..'
$outDir = $LogsDiagnosticsDir
if (-not $outDir) { $outDir = Join-Path (Join-Path $repoRoot 'logs') 'diagnostics' }
$outDir = [System.IO.Path]::GetFullPath($outDir)
if ($outDir -match '\.json$') { $outDir = Split-Path -Parent $outDir }
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$blockersPath = Join-Path $outDir 'blockers.json'

# Build JSON object
$out = [PSCustomObject]@{
    PortLockResults   = @($portLockResults)
    BlockerProcesses  = @($blockerProcesses)
    BlockerServices   = @($blockerServices)
    AutoStopRequested = $AutoStopStarkeyInspire
    IsElevated        = $isAdmin
    StoppedServices   = @($stoppedServices)
    StoppedProcesses  = @($stoppedProcesses)
    RemainedServices  = @($stillServices)
    RemainedProcesses = @($stillRunning)
}
$blockersJsonOut = $out | ConvertTo-Json -Depth 5 -Compress
Set-Content -Path $blockersPath -Value $blockersJsonOut -Encoding UTF8 -ErrorAction SilentlyContinue

if ($JsonOnly) {
    Write-Output $blockersJsonOut
    return
}

# --- Output (human-readable) ---
Write-Host "========== PORT LOCK STATUS ==========" -ForegroundColor Cyan
$portLockResults | Format-Table -AutoSize PortName, Locked, Exception -Wrap | Out-String | Write-Host

Write-Host "========== BLOCKER PROCESSES ==========" -ForegroundColor Cyan
if ($blockerProcesses.Count -eq 0) {
    Write-Host "  None found." -ForegroundColor Green
} else {
    $blockerProcesses | Format-Table -AutoSize ProcessName, Id, Path -Wrap | Out-String | Write-Host
}

Write-Host "========== BLOCKER SERVICES ==========" -ForegroundColor Cyan
if ($blockerServices.Count -eq 0) {
    Write-Host "  None found." -ForegroundColor Green
} else {
    $blockerServices | Format-Table -AutoSize ServiceName, DisplayName, Status, StartType -Wrap | Out-String | Write-Host
}

if ($AutoStopStarkeyInspire) {
    Write-Host "========== AUTO-STOP (Starkey/Inspire only) ==========" -ForegroundColor Cyan
    Write-Host "Stopped services: $(if ($stoppedServices.Count -eq 0) { 'None' } else { $stoppedServices -join ', ' })"
    Write-Host "Stopped processes: $(if ($stoppedProcesses.Count -eq 0) { 'None' } else { $stoppedProcesses -join '; ' })"
    Write-Host "Remained / failed to stop - services: $(if ($stillServices.Count -eq 0) { 'None' } else { $stillServices -join ', ' })"
    Write-Host "Remained / failed to stop - processes: $(if ($stillRunning.Count -eq 0) { 'None' } else { $stillRunning -join '; ' })"
}

Write-Host "Written: $blockersPath" -ForegroundColor Gray
Write-Host "========== PORT BLOCKERS (JSON) ==========" -ForegroundColor Cyan
Write-Output $blockersJsonOut
