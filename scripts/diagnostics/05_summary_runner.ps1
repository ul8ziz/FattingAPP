# 05_summary_runner.ps1 - Run 01-04 (file-based), generate docs\HI-PRO_Diagnostic_Report.md and docs\reports\HI-PRO_Report_YYYYMMDD_HHMMSS.md
# When -ReportToLogsDiagnostics: also write logs\diagnostics\HI-PRO_DiagnosticReport.md (for app startup preflight).
# Usage: .\scripts\diagnostics\05_summary_runner.ps1 -AppOutputDir "src\App\bin\Debug\net10.0-windows" [-AutoStopStarkeyInspire] [-ReportToLogsDiagnostics]

param(
    [string]$AppOutputDir = $null,
    [switch]$AutoStopStarkeyInspire = $false,
    [switch]$ReportToLogsDiagnostics = $false
)

$ErrorActionPreference = 'Continue'
$scriptDir = $PSScriptRoot
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir '..\..'))
$docsDir = Join-Path $repoRoot 'docs'
$logsDir = Join-Path $repoRoot 'logs'
$diagDir = [System.IO.Path]::GetFullPath((Join-Path $logsDir 'diagnostics'))
$reportsDir = Join-Path $docsDir 'reports'

foreach ($d in @($docsDir, $logsDir, $diagDir, $reportsDir)) {
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

$commandsExecuted = [System.Collections.ArrayList]::new()
$errorsCollected = [System.Collections.ArrayList]::new()

function Run-Script {
    param([string]$ScriptPath, [array]$ArgsList)
    $cmd = "powershell.exe -ExecutionPolicy Bypass -NoProfile -File `"$ScriptPath`""
    foreach ($a in $ArgsList) {
        if ($a -match '\s') { $cmd += " `"$a`"" } else { $cmd += " $a" }
    }
    [void]$commandsExecuted.Add($cmd)
    try {
        $out = & $ScriptPath @ArgsList 2>&1
        foreach ($line in $out) {
            $s = $line.ToString().Trim()
            if ($s.Length -eq 0) { continue }
            if ($s.StartsWith('{') -or $s.StartsWith('[')) { continue }
            if ($s -match '^\s*\{' -or $s -match '"PortName"|"BlockerProcesses"|"PerPortResults"') { continue }
            if ($s -match '^(Error|Exception|At line|Cannot convert|The term .* is not recognized)' -or $s -match 'FullyQualifiedErrorId') { [void]$errorsCollected.Add($s) }
        }
        return $true
    } catch {
        [void]$errorsCollected.Add("$ScriptPath : $_")
        return $false
    }
}

$portsJsonPath = Join-Path $diagDir 'ports.json'

# --- Run 01 (writes ports.json) ---
Run-Script -ScriptPath "$scriptDir\01_ports_inventory.ps1" -ArgsList @('-LogsDiagnosticsDir', $diagDir) | Out-Null

# --- Run 02 (reads ports.json path) ---
Run-Script -ScriptPath "$scriptDir\02_handshake_tests.ps1" -ArgsList @('-PortsJson', $portsJsonPath, '-LogsDiagnosticsDir', $diagDir) | Out-Null

# --- Run 03 ---
$args03 = @('-LogsDiagnosticsDir', $diagDir)
if ($AppOutputDir) { $args03 += @('-AppOutputDir', $AppOutputDir) }
Run-Script -ScriptPath "$scriptDir\03_dll_integrity.ps1" -ArgsList $args03 | Out-Null

# --- Run 04 ---
$args04 = @('-PortsJson', $portsJsonPath, '-LogsDiagnosticsDir', $diagDir)
if ($AutoStopStarkeyInspire) { $args04 += '-AutoStopStarkeyInspire' }
Run-Script -ScriptPath "$scriptDir\04_port_blockers.ps1" -ArgsList $args04 | Out-Null

# --- Read JSON from files ---
$portsInventory = @()
$handshakeData = $null
$dllData = $null
$blockersData = $null

if (Test-Path -LiteralPath $portsJsonPath -PathType Leaf) {
    try {
        $raw = Get-Content -LiteralPath $portsJsonPath -Raw -Encoding UTF8
        $portsInventory = $raw | ConvertFrom-Json
        if (-not ($portsInventory -is [array]) -and $portsInventory.PSObject.Properties['value']) { $portsInventory = @($portsInventory.value) }
        elseif (-not ($portsInventory -is [array])) { $portsInventory = @($portsInventory) }
    } catch { [void]$errorsCollected.Add("Read ports.json: $_") }
}

$handshakePath = Join-Path $diagDir 'handshake.json'
if (Test-Path -LiteralPath $handshakePath -PathType Leaf) {
    try { $handshakeData = Get-Content -LiteralPath $handshakePath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { [void]$errorsCollected.Add("Read handshake.json: $_") }
}

$dllsPath = Join-Path $diagDir 'dlls.json'
if (Test-Path -LiteralPath $dllsPath -PathType Leaf) {
    try { $dllData = Get-Content -LiteralPath $dllsPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { [void]$errorsCollected.Add("Read dlls.json: $_") }
}

$blockersPath = Join-Path $diagDir 'blockers.json'
if (Test-Path -LiteralPath $blockersPath -PathType Leaf) {
    try { $blockersData = Get-Content -LiteralPath $blockersPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { [void]$errorsCollected.Add("Read blockers.json: $_") }
}

# --- Build Markdown ---
$md = @()
$md += "# HI-PRO Diagnostic Report"
$md += ""
$md += "Generated: **$((Get-Date).ToString('o'))**"
if ($AppOutputDir) { $md += " | App output dir: $AppOutputDir" }
$md += ""

# 1) Ports inventory (COM + FriendlyName; HI-PRO/FTDI: InstanceId, Manufacturer, DriverService, DriverVersion, Provider, INF; SerialNumber/Description)
$md += "## 1. Ports inventory"
$md += ""
if ($portsInventory -and $portsInventory.Count -gt 0) {
    $md += "| PortName | FriendlyName | Manufacturer | DriverService | DriverVersion |"
    $md += "|----------|--------------|---------------|---------------|---------------|"
    foreach ($p in $portsInventory) {
        $pn = $p.PortName; if (-not $pn) { $pn = '' }
        $fn = $p.FriendlyName; if (-not $fn) { $fn = '' }; $fn = $fn -replace '\|','\'
        $man = $p.Manufacturer; if (-not $man) { $man = '' }; $man = $man -replace '\|','\'
        $svc = $p.DriverService; if (-not $svc) { $svc = '' }; $svc = $svc -replace '\|','\'
        $ver = $p.DriverVersion; if (-not $ver) { $ver = '' }; $ver = $ver -replace '\|','\'
        $md += "| $pn | $fn | $man | $svc | $ver |"
    }
    $md += ""
    $md += "SerialNumber / Description evidence:"
    $md += '```'
    foreach ($p in $portsInventory) {
        $line = "Port: $($p.PortName) | FriendlyName: $($p.FriendlyName) | InstanceId: $($p.InstanceId) | DriverService: $($p.DriverService) | SerialNumber: $($p.SerialNumber) | ContainerId: $($p.ContainerId)"
        $md += $line
    }
    $md += '```'
} else {
    $md += "No ports found in inventory."
}
$md += ""

# 2) Handshake/connectivity (always render table from PerPortResults or Summary)
$md += "## 2. Handshake / connectivity attempts"
$md += ""
$md += "*This is a basic open/close test only; it does NOT prove HI-PRO protocol handshake.*"
$md += ""
$perPort = $null
if ($handshakeData) {
    if ($handshakeData.PerPortResults) { $perPort = @($handshakeData.PerPortResults) }
    elseif ($handshakeData.Summary) { $perPort = @($handshakeData.Summary) }
}
if ($perPort -and $perPort.Count -gt 0) {
    $md += "| Port | Success | Detail |"
    $md += "|------|---------|--------|"
    foreach ($r in $perPort) {
        $detail = $r.Detail; if (-not $detail) { $detail = '' }; $detail = ($detail -replace '\|','\' -replace "`r`n", ' ').Trim()
        $md += "| $($r.PortName) | $($r.Success) | $detail |"
    }
} else {
    $md += "No handshake data (file missing or invalid)."
}
$md += ""

# 3) Port lock status
$md += "## 3. Port lock status"
$md += ""
if ($handshakeData -and $handshakeData.COM2) {
    $c2 = $handshakeData.COM2
    $md += "- **COM2 exists:** Yes"
    $md += "- **COM2 open (basic test):** $($c2.OpenOK)"
    $md += "- **COM2 status:** $($c2.Status)"
    $md += "- **Note:** $($c2.Note)"
}
$com2Device = $portsInventory | Where-Object { $_.PortName -eq 'COM2' } | Select-Object -First 1
if ($com2Device) {
    $md += "- **COM2 device service (driver):** $($com2Device.DriverService)"
} else {
    $md += "- **COM2:** Not in port inventory."
}
if ($blockersData -and $blockersData.PortLockResults) {
    $md += ""
    $md += "Port lock test:"
    foreach ($pl in $blockersData.PortLockResults) {
        $lockStr = if ($pl.Locked) { 'LOCKED' } else { 'Free' }
        $exStr = ''; if ($pl.Exception) { $exStr = " ($($pl.Exception))" }
        $md += "- $($pl.PortName): $lockStr$exStr"
    }
}
$md += ""

# 4) Blocker processes/services (always render tables; show blockers even when AutoStop not requested)
$md += "## 4. Blocker processes and services"
$md += ""
if ($blockersData) {
    $bpList = @($blockersData.BlockerProcesses)
    $bsList = @($blockersData.BlockerServices)
    if ($bpList.Count -gt 0) {
        $md += "**BlockerProcesses**"
        $md += "| ProcessName | Id | Path |"
        $md += "|-------------|-----|------|"
        foreach ($bp in $bpList) {
            $pathVal = $bp.Path; if (-not $pathVal) { $pathVal = '' }; $pathVal = ($pathVal -replace '\|','\').Trim()
            $md += "| $($bp.ProcessName) | $($bp.Id) | $pathVal |"
        }
        $md += ""
    } else {
        $md += "**BlockerProcesses:** None found."
        $md += ""
    }
    if ($bsList.Count -gt 0) {
        $md += "**BlockerServices**"
        $md += "| ServiceName | DisplayName | Status | StartType |"
        $md += "|-------------|-------------|--------|-----------|"
        foreach ($bs in $bsList) {
            $md += "| $($bs.ServiceName) | $($bs.DisplayName) | $($bs.Status) | $($bs.StartType) |"
        }
        $md += ""
    } else {
        $md += "**BlockerServices:** None found."
        $md += ""
    }
    if ($blockersData.AutoStopRequested) {
        $md += "**Auto-stop (Starkey/Inspire only):**"
        $md += "- IsElevated: $($blockersData.IsElevated)"
        $ss = 'None'; $sarr = @($blockersData.StoppedServices); if ($sarr.Count -gt 0) { $ss = ($sarr -join ', ') }
        $sp = 'None'; $parr = @($blockersData.StoppedProcesses); if ($parr.Count -gt 0) { $sp = ($parr -join '; ') }
        $rs = 'None'; $rarr = @($blockersData.RemainedServices); if ($rarr.Count -gt 0) { $rs = ($rarr -join ', ') }
        $rp = 'None'; $rparr = @($blockersData.RemainedProcesses); if ($rparr.Count -gt 0) { $rp = ($rparr -join '; ') }
        $md += "- StoppedServices: $ss"
        $md += "- StoppedProcesses: $sp"
        $md += "- RemainedServices: $rs"
        $md += "- RemainedProcesses: $rp"
        $md += ""
    }
} else {
    $md += "No blocker data (file missing or invalid)."
}
$md += ""

# 5) DLL integrity / compatibility (AppBitness from app EXE PE; PowerShellBitness separate)
$md += "## 5. DLL integrity / compatibility"
$md += ""
if ($dllData) {
    $appBit = $dllData.AppBitness; if (-not $appBit) { $appBit = 'Unknown' }
    $psBit = $dllData.PowerShellBitness; if (-not $psBit) { $psBit = $dllData.ProcessArchitecture }
    $md += "- **AppBitness** (from app EXE PE): **$appBit**"
    $md += "- **AppExePath:** $($dllData.AppExePath)"
    $md += "- **PowerShellBitness:** $psBit (diagnostics runner process; not the app)"
    $md += ""
    $dllEntries = @($dllData.DllEntries)
    if ($dllEntries.Count -gt 0) {
        $md += "| Location | FileName | Size | PE | FileVersion | SHA256 |"
        $md += "|----------|----------|------|-----|-------------|--------|"
        foreach ($e in $dllEntries) {
            $sha = $e.SHA256
            if ($sha -and $sha.Length -gt 16) { $sha = $sha.Substring(0,16) + '...' }
            $md += "| $($e.LocationLabel) | $($e.FileName) | $($e.Size) | $($e.PEArchitecture) | $($e.FileVersion) | $sha |"
        }
    }
    $warnList = @($dllData.Warnings)
    if ($warnList.Count -gt 0) {
        $md += ""
        $md += "**Warnings:**"
        foreach ($w in $warnList) { $md += "- $w" }
    }
} else {
    $md += "No DLL integrity data."
}
$md += ""

# Build conclusions list (output under section 7)
$conclusions = [System.Collections.ArrayList]::new()
if ($handshakeData -and $handshakeData.COM2 -and -not $handshakeData.COM2.OpenOK) {
    [void]$conclusions.Add("**COM2 is not open** (basic test). Evidence: $($handshakeData.COM2.Detail). Likely cause: port in use by another process or COM2 not present.")
}
$bpCount = if ($blockersData -and $blockersData.BlockerProcesses) { @($blockersData.BlockerProcesses).Count } else { 0 }
if ($bpCount -gt 0) {
    [void]$conclusions.Add("**Starkey/Inspire or related processes** are running. Evidence: $bpCount process(es) matching blocker pattern. These can hold COM2 and prevent HI-PRO handshake.")
}
$bsCount = if ($blockersData -and $blockersData.BlockerServices) { @($blockersData.BlockerServices).Count } else { 0 }
if ($bsCount -gt 0) {
    [void]$conclusions.Add("**Starkey/Inspire or related services** are present. Evidence: $bsCount service(s). Stopping them (e.g. with -AutoStopStarkeyInspire when elevated) may free the port.")
}
if ($dllData -and $dllData.Warnings -and $dllData.Warnings.Count -gt 0) {
    [void]$conclusions.Add("**DLL/architecture mismatch.** Evidence: $($dllData.Warnings -join ' '). Ensure app runs as x86 when using HI-PRO/CTK x86 DLLs.")
}
$com2Missing = -not ($portsInventory | Where-Object { $_.PortName -eq 'COM2' })
if ($com2Missing) {
    [void]$conclusions.Add("**COM2 does not exist** in port inventory. HI-PRO may be on another COM port or driver not installed.")
}
$com2Ftser2k = $portsInventory | Where-Object { $_.PortName -eq 'COM2' -and $_.DriverService -eq 'FTSER2K' } | Select-Object -First 1
if ($com2Ftser2k -and $handshakeData -and $handshakeData.COM2 -and -not $handshakeData.COM2.OpenOK) {
    [void]$conclusions.Add("**VCP vs D2XX mode:** COM2 exists with driver FTSER2K but open failed. If D2XX device count is 0 (e.g. from Ftd2xxTest), consider VCP vs D2XX mode mismatch as likely cause.")
}
while ($conclusions.Count -lt 3) {
    [void]$conclusions.Add("(No additional evidence-based conclusion)")
    if ($conclusions.Count -ge 3) { break }
}

# 6) Evidence (commands + errors)
$md += "## 6. Evidence"
$md += ""
$md += "### PowerShell commands executed"
$md += '```'
foreach ($cmd in $commandsExecuted) {
    $md += $cmd
}
$md += '```'
$md += ""
$md += "### Errors (captured during run)"
$md += '```'
if ($errorsCollected.Count -eq 0) {
    $md += "(none captured)"
} else {
    foreach ($err in $errorsCollected) { $md += $err }
}
$md += '```'
$md += ""

# 7) Conclusions (top 3, each tied to evidence)
$md += "## 7. Conclusions"
$md += ""
for ($i = 0; $i -lt [Math]::Min(3, $conclusions.Count); $i++) {
    $md += "$($i+1). $($conclusions[$i])"
}
$md += ""
$md += "**Recommended fixes (project-only first):**"
$md += ""
$md += '1. **Run diagnostics from repo root:** ``.\scripts\diagnostics\05_summary_runner.ps1 -AppOutputDir "src\App\bin\Debug\net10.0-windows"``'
$md += '2. **If COM2 is locked:** Close Inspire/Starkey apps; or run with ``-AutoStopStarkeyInspire`` (elevated) to stop only Starkey/Inspire.'
$md += "3. **If COM2 missing:** Reassign HI-PRO to COM2 in Device Manager (Port Settings -> Advanced) if possible."
$md += "4. **If DLL/arch mismatch:** Build and run the app as **x86**; do not modify system DLLs."
$md += '5. **VCP vs D2XX:** If COM2 exists (FTSER2K) but SDK reports 0 D2XX devices, run Ftd2xxTest; consider driver mode mismatch.'
$md += ""

$mdText = $md -join "`r`n"

# --- Write report files (exact names per spec) ---
$reportPathMd = Join-Path $docsDir 'HI-PRO_Diagnostic_Report.md'
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$reportTimestampedPath = Join-Path $reportsDir "HI-PRO_Report_$timestamp.md"

Set-Content -Path $reportPathMd -Value $mdText -Encoding UTF8
Set-Content -Path $reportTimestampedPath -Value $mdText -Encoding UTF8

if ($ReportToLogsDiagnostics) {
    $logsDiagReportPath = Join-Path $diagDir 'HI-PRO_DiagnosticReport.md'
    Set-Content -Path $logsDiagReportPath -Value $mdText -Encoding UTF8
    Write-Host "  $logsDiagReportPath"
}

Write-Host "Reports written:" -ForegroundColor Green
Write-Host "  $reportPathMd"
Write-Host "  $reportTimestampedPath"
Write-Host "  JSON: $diagDir\ports.json, handshake.json, dlls.json, blockers.json"
