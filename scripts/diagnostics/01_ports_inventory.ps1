# 01_ports_inventory.ps1 - Enumerate ALL serial/USB/FTDI related devices.
# Output: JSON to logs\diagnostics\ports.json and object to pipeline.

param(
    [string]$RepoRoot = $PSScriptRoot + '\..\..',
    [string]$LogsDiagnosticsDir = $null,
    [switch]$JsonOnly = $false
)

$ErrorActionPreference = 'Continue'

# --- Collect ports from .NET ---
$dotNetPorts = @()
try {
    $dotNetPorts = [System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object { [int]($_ -replace 'COM', '') }
} catch {
    $dotNetPorts = @()
}

# --- PnP Ports (Class Ports) ---
$pnpPorts = @()
try {
    $pnpPorts = Get-PnpDevice -Class Ports -ErrorAction SilentlyContinue | Where-Object { $_.Status -ne 'Unknown' }
} catch {
    # Fallback: no PnP
}

# --- Win32_SerialPort (WMI/CIM) ---
$serialPorts = @()
try {
    $serialPorts = Get-CimInstance -ClassName Win32_SerialPort -ErrorAction SilentlyContinue
} catch {
    $serialPorts = @()
}

# --- USB / PnP entities matching HI-PRO/FTDI/Starkey/Inspire ---
$matchPattern = 'FTDI|HI-PRO|VID_0C33|PID_0012|Starkey|Inspire'
$usbRelated = @()
try {
    $all = Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction SilentlyContinue
    $usbRelated = $all | Where-Object {
        $_.Name -match $matchPattern -or
        $_.PNPDeviceID -match $matchPattern -or
        ($_.Name -match 'COM\d+' -and ($_.Name -match 'Serial|USB|FTDI|HI-PRO'))
    }
} catch {
    $usbRelated = @()
}

# --- Build unified inventory: one object per port/device ---
$inventory = [System.Collections.ArrayList]::new()
$seenPorts = @{}

function Get-DeviceDriverInfo {
    param([string]$InstanceId)
    $svc = $null
    $ver = $null
    $prov = $null
    $inf = $null
    try {
        $dev = Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName 'DEVPKEY_Device_DriverDesc' -ErrorAction SilentlyContinue
        if ($dev) { $svc = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName 'DEVPKEY_Device_Service' -ErrorAction SilentlyContinue).Data }
        $dv  = Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName 'DEVPKEY_Device_DriverVersion' -ErrorAction SilentlyContinue
        if ($dv) { $ver = $dv.Data }
        $dp  = Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName 'DEVPKEY_Device_DriverProvider' -ErrorAction SilentlyContinue
        if ($dp) { $prov = $dp.Data }
        $di  = Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName 'DEVPKEY_Device_DriverInfPath' -ErrorAction SilentlyContinue
        if ($di) { $inf = $di.Data }
    } catch { }
    return @{ DriverService = $svc; DriverVersion = $ver; DriverProvider = $prov; InfPath = $inf }
}

# Add from Win32_SerialPort (has PortName, DeviceID, etc.)
foreach ($sp in $serialPorts) {
    $portName = $sp.DeviceID
    if (-not $portName -or $seenPorts[$portName]) { continue }
    $seenPorts[$portName] = $true
    $pnpId = $sp.PNPDeviceID
    $drv = Get-DeviceDriverInfo -InstanceId $pnpId
    $o = [PSCustomObject]@{
        PortName       = $portName
        FriendlyName   = $sp.Name
        Manufacturer   = $sp.Name
        PNPDeviceID    = $pnpId
        InstanceId     = $pnpId
        DriverService  = $drv.DriverService
        DriverVersion  = $drv.DriverVersion
        DriverProvider = $drv.DriverProvider
        InfPath        = $drv.InfPath
        SerialNumber   = $null
        ContainerId    = $null
        Source         = 'Win32_SerialPort'
    }
    [void]$inventory.Add($o)
}

# Add from PnP Ports (match by friendly name COMx)
foreach ($p in $pnpPorts) {
    $name = $p.FriendlyName
    $portName = $null
    if ($name -match '\((COM\d+)\)') { $portName = $Matches[1] }
    if (-not $portName) { continue }
    if ($seenPorts[$portName]) { continue }
    $seenPorts[$portName] = $true
    $drv = Get-DeviceDriverInfo -InstanceId $p.InstanceId
    $o = [PSCustomObject]@{
        PortName       = $portName
        FriendlyName   = $name
        Manufacturer   = $p.Manufacturer
        PNPDeviceID    = $p.InstanceId
        InstanceId     = $p.InstanceId
        DriverService  = $drv.DriverService
        DriverVersion  = $drv.DriverVersion
        DriverProvider = $drv.DriverProvider
        InfPath        = $drv.InfPath
        SerialNumber   = $null
        ContainerId    = $null
        Source         = 'PnpDevice-Ports'
    }
    [void]$inventory.Add($o)
}

# Add .NET ports not yet in inventory
foreach ($portName in $dotNetPorts) {
    if ($seenPorts[$portName]) { continue }
    $seenPorts[$portName] = $true
    $o = [PSCustomObject]@{
        PortName       = $portName
        FriendlyName   = $portName
        Manufacturer   = $null
        PNPDeviceID    = $null
        InstanceId     = $null
        DriverService  = $null
        DriverVersion  = $null
        DriverProvider = $null
        InfPath        = $null
        SerialNumber   = $null
        ContainerId    = $null
        Source         = 'SerialPort.GetPortNames'
    }
    [void]$inventory.Add($o)
}

# Enrich with USB-related devices (may not have COM but are HI-PRO/FTDI)
foreach ($u in $usbRelated) {
    $id = $u.DeviceID
    $name = $u.Name
    $portName = $null
    if ($name -match '\((COM\d+)\)') { $portName = $Matches[1] }
    $drv = Get-DeviceDriverInfo -InstanceId $id
    $serial = $null
    $container = $null
    try {
        $serial = (Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_SerialNumber' -ErrorAction SilentlyContinue).Data
        $container = (Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_ContainerId' -ErrorAction SilentlyContinue).Data
    } catch { }
    if ($portName -and -not $seenPorts[$portName]) {
        $seenPorts[$portName] = $true
        $o = [PSCustomObject]@{
            PortName       = $portName
            FriendlyName   = $name
            Manufacturer   = $u.Manufacturer
            PNPDeviceID    = $id
            InstanceId     = $id
            DriverService  = $drv.DriverService
            DriverVersion  = $drv.DriverVersion
            DriverProvider = $drv.DriverProvider
            InfPath        = $drv.InfPath
            SerialNumber   = $serial
            ContainerId    = $container
            Source         = 'Win32_PnPEntity-USB'
        }
        [void]$inventory.Add($o)
    } else {
        # No COM but HI-PRO/FTDI device
        $key = "USB-$id"
        if (-not $seenPorts[$key]) {
            $seenPorts[$key] = $true
            $o = [PSCustomObject]@{
                PortName       = $portName
                FriendlyName   = $name
                Manufacturer   = $u.Manufacturer
                PNPDeviceID    = $id
                InstanceId     = $id
                DriverService  = $drv.DriverService
                DriverVersion  = $drv.DriverVersion
                DriverProvider = $drv.DriverProvider
                InfPath        = $drv.InfPath
                SerialNumber   = $serial
                ContainerId    = $container
                Source         = 'Win32_PnPEntity-USB'
            }
            [void]$inventory.Add($o)
        }
    }
}

# Sort by PortName (COM number)
$inventory = @($inventory | Sort-Object {
    if ($_.PortName -match '^COM(\d+)$') { [int]$Matches[1] } else { 9999 }
})

$json = $inventory | ConvertTo-Json -Depth 5 -Compress
$outDir = $LogsDiagnosticsDir
if (-not $outDir) { $outDir = Join-Path (Join-Path $RepoRoot 'logs') 'diagnostics' }
$outDir = [System.IO.Path]::GetFullPath($outDir)
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$portsJsonPath = Join-Path $outDir 'ports.json'
Set-Content -Path $portsJsonPath -Value $json -Encoding UTF8 -ErrorAction SilentlyContinue

if ($JsonOnly) {
    Write-Output $inventory
    return
}
Write-Host "========== PORTS INVENTORY (human-readable) ==========" -ForegroundColor Cyan
$inventory | Format-Table -AutoSize PortName, FriendlyName, Manufacturer, DriverService, DriverVersion -Wrap | Out-String | Write-Host
Write-Host "========== PORTS INVENTORY (JSON) ==========" -ForegroundColor Cyan
Write-Host "Written: $portsJsonPath"
Write-Output $inventory
