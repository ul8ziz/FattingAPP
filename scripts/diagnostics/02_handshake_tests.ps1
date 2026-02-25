# 02_handshake_tests.ps1 - Basic connectivity/lock test per COM port (NOT full HI-PRO handshake).
# Input: -PortsJson <path> (file path). Output: logs\diagnostics\handshake.json

param(
    [string]$PortsJson = $null,   # Path to ports.json file
    [string]$LogsDiagnosticsDir = $null,
    [switch]$JsonOnly = $false
)

$ErrorActionPreference = 'Continue'

# Get port list from file or .NET
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
    } catch {
        $portList = @()
    }
}
if ($portList.Count -eq 0) {
    try {
        $portList = [System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object { [int]($_ -replace 'COM', '') }
    } catch {
        $portList = @()
    }
}

$results = [System.Collections.ArrayList]::new()
$openTimeoutMs = 500
$holdMs = 400

function Test-ComOpen {
    param([string]$PortName, [int]$BaudRate)
    $errMsg = $null
    $ok = $false
    try {
        $parity = [System.IO.Ports.Parity]::None
        $stopBits = [System.IO.Ports.StopBits]::One
        $port = New-Object System.IO.Ports.SerialPort($PortName, $BaudRate, $parity, 8, $stopBits)
        $port.ReadTimeout = 300
        $port.WriteTimeout = 300
        $port.Open()
        Start-Sleep -Milliseconds $holdMs
        $port.Close()
        $port.Dispose()
        $ok = $true
    } catch {
        $errMsg = $_.Exception.Message
        try { if ($port) { $port.Close(); $port.Dispose() } } catch { }
    }
    return @{ Success = $ok; ExceptionMessage = $errMsg }
}

# --- Per-port tests: build PerPortResults (PortName, Baud, Success, Exception) and Summary ---
$perPortDetailed = [System.Collections.ArrayList]::new()
$summaryList = [System.Collections.ArrayList]::new()
foreach ($portName in $portList) {
    $r9600 = Test-ComOpen -PortName $portName -BaudRate 9600
    [void]$perPortDetailed.Add([PSCustomObject]@{
        PortName  = $portName
        Baud      = 9600
        Success   = $r9600.Success
        Exception = $r9600.ExceptionMessage
    })
    if (-not $r9600.Success) {
        $r115200 = Test-ComOpen -PortName $portName -BaudRate 115200
        [void]$perPortDetailed.Add([PSCustomObject]@{
            PortName  = $portName
            Baud      = 115200
            Success   = $r115200.Success
            Exception = $r115200.ExceptionMessage
        })
        $success = $r115200.Success
        $detail = "9600: $($r9600.ExceptionMessage); 115200: " + $(if ($r115200.Success) { "OK" } else { $r115200.ExceptionMessage })
    } else {
        $success = $true
        $detail = "9600: OK"
    }
    [void]$summaryList.Add([PSCustomObject]@{
        PortName  = $portName
        Success   = $success
        Status    = if ($success) { "OK" } else { "FAILED" }
        Detail    = $detail
    })
    $o = [PSCustomObject]@{
        PortName   = $portName
        Success    = $success
        Detail     = $detail
        Exception  = if ($success) { $null } else { $r9600.ExceptionMessage }
    }
    [void]$results.Add($o)
}

# --- COM2 special section (HI-PRO expected) ---
$com2Result = $null
if ($portList -contains 'COM2') {
    $r = $results | Where-Object { $_.PortName -eq 'COM2' } | Select-Object -First 1
    $com2OpenOk = $r.Success
    $com2Status = if ($r.Success) { "COM2 open OK" } else {
        if ($r.Exception -match 'Access is denied|being used|in use') { "LOCKED" } else { "FAILED" }
    }
    $com2Result = [PSCustomObject]@{
        PortName   = 'COM2'
        OpenOK     = $com2OpenOk
        Status     = $com2Status
        Detail     = $r.Detail
        Note       = "Basic connectivity/lock test only; does NOT prove HI-PRO handshake."
    }
} else {
    $com2Result = [PSCustomObject]@{
        PortName   = 'COM2'
        OpenOK     = $false
        Status     = "NOT_PRESENT"
        Detail     = "COM2 not in port list."
        Note       = "Basic connectivity/lock test only; does NOT prove HI-PRO handshake."
    }
}

$out = [PSCustomObject]@{
    PerPortResults = @($results)
    PerPortDetailed = @($perPortDetailed)
    Summary        = @($summaryList)
    COM2           = $com2Result
}
$handshakeJsonOut = $out | ConvertTo-Json -Depth 5 -Compress
$repoRoot = Join-Path $PSScriptRoot '..\..'
$outDir = $LogsDiagnosticsDir
if (-not $outDir) { $outDir = Join-Path (Join-Path $repoRoot 'logs') 'diagnostics' }
$outDir = [System.IO.Path]::GetFullPath($outDir)
if ($outDir -match '\.json$') { $outDir = Split-Path -Parent $outDir }
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$handshakePath = Join-Path $outDir 'handshake.json'
Set-Content -Path $handshakePath -Value $handshakeJsonOut -Encoding UTF8 -ErrorAction SilentlyContinue

if ($JsonOnly) {
    Write-Output $handshakeJsonOut
    return
}
Write-Host "========== HANDSHAKE/CONNECTIVITY ATTEMPTS (per port) ==========" -ForegroundColor Cyan
Write-Host "NOTE: This is a basic open/close test only. It does NOT prove HI-PRO protocol handshake." -ForegroundColor Yellow
$results | Format-Table -AutoSize PortName, Success, Detail -Wrap | Out-String | Write-Host
Write-Host "========== COM2 (HI-PRO expected) ==========" -ForegroundColor Cyan
$com2Color = 'Green'; if (-not $com2Result.OpenOK) { $com2Color = 'Red' }
Write-Host "Status: $($com2Result.Status) | OpenOK: $($com2Result.OpenOK) | $($com2Result.Detail)" -ForegroundColor $com2Color
Write-Host "Note: $($com2Result.Note)" -ForegroundColor Gray
Write-Host "Written: $handshakePath" -ForegroundColor Gray
Write-Output $handshakeJsonOut
