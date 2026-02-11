# check-ftdi.ps1 - Locate ftd2xx.dll and related FTDI/HI-PRO DLLs; report versions and locations.
# Do NOT modify or delete system DLLs. Report only.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\check-ftdi.ps1 [-AppOutputPath "path\to\bin\Debug\net10.0-windows"]

param([string]$AppOutputPath)

$ErrorActionPreference = "Continue"
$report = @()

function Get-FileInfoRow {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        return [PSCustomObject]@{ Location = $Label; FullPath = $Path; Exists = $false; Length = $null; LastWriteTime = $null; VersionInfo = $null }
    }
    $item = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    $ver = $null
    try { $ver = $item.VersionInfo.FileVersion } catch {}
    return [PSCustomObject]@{
        Location = $Label
        FullPath = $Path
        Exists   = $true
        Length   = $item.Length
        LastWriteTime = $item.LastWriteTime.ToString("o")
        VersionInfo   = $ver
    }
}

$report += "=== check-ftdi.ps1 run at $(Get-Date -Format 'o') ==="
$report += ""

# 1) where.exe ftd2xx.dll (PATH search)
$report += "--- where.exe ftd2xx.dll ---"
try {
    $whereOut = & where.exe ftd2xx.dll 2>&1
    $report += if ($whereOut) { $whereOut } else { "(no result)" }
} catch {
    $report += "where.exe failed: $_"
}
$report += ""

# 2) Get-Item for each path from where (if any)
$report += "--- Get-Item for discovered ftd2xx.dll paths ---"
$paths = @()
try {
    $whereOut = & where.exe ftd2xx.dll 2>&1 | Where-Object { $_ -match '\S' }
    if ($whereOut) { $paths = @($whereOut) }
} catch {}
foreach ($p in $paths) {
    $p = $p.Trim()
    if ([string]::IsNullOrWhiteSpace($p)) { continue }
    $row = Get-FileInfoRow -Path $p -Label "where"
    $report += "Path: $($row.FullPath) | Exists: $($row.Exists) | Length: $($row.Length) | LastWrite: $($row.LastWriteTime) | Version: $($row.VersionInfo)"
}
if ($paths.Count -eq 0) { $report += "(no paths from where.exe)" }
$report += ""

# 3) Explicit paths
$report += "--- Explicit path checks ---"
$explicit = @(
    @{ Label = "System32";     Path = "C:\Windows\System32\ftd2xx.dll" },
    @{ Label = "SysWOW64";    Path = "C:\Windows\SysWOW64\ftd2xx.dll" },
    @{ Label = "HI-PRO";      Path = "C:\Program Files (x86)\HI-PRO\ftd2xx.dll" },
    @{ Label = "HI-PRO FTD2XX_NET"; Path = "C:\Program Files (x86)\HI-PRO\FTD2XX_NET.dll" },
    @{ Label = "HI-PRO FTChipID";   Path = "C:\Program Files (x86)\HI-PRO\FTChipID.dll" }
)
foreach ($e in $explicit) {
    $row = Get-FileInfoRow -Path $e.Path -Label $e.Label
    $report += "$($e.Label): $($e.Path) | Exists: $($row.Exists) | Length: $($row.Length) | LastWrite: $($row.LastWriteTime) | Version: $($row.VersionInfo)"
}
if ($AppOutputPath -and (Test-Path -LiteralPath $AppOutputPath)) {
    $appFtd2xx = Join-Path $AppOutputPath "ftd2xx.dll"
    $appFtd2xxNet = Join-Path $AppOutputPath "FTD2XX_NET.dll"
    $row = Get-FileInfoRow -Path $appFtd2xx -Label "AppOutput"
    $report += "AppOutput ftd2xx: $appFtd2xx | Exists: $($row.Exists) | Length: $($row.Length) | LastWrite: $($row.LastWriteTime)"
    $row2 = Get-FileInfoRow -Path $appFtd2xxNet -Label "AppOutput"
    $report += "AppOutput FTD2XX_NET: $appFtd2xxNet | Exists: $($row2.Exists) | Length: $($row2.Length) | LastWrite: $($row2.LastWriteTime)"
}
$report += ""

# 4) Note on System32
$sys32 = "C:\Windows\System32\ftd2xx.dll"
if (Test-Path -LiteralPath $sys32) {
    $report += "NOTE: ftd2xx.dll exists in System32. This can be loaded before app/HI-PRO folder. Do NOT delete or modify system DLLs; use SetDllDirectory(app folder) at app startup to prefer app copy."
}
$report += ""
$report += "=== end check-ftdi ==="
$report -join "`r`n" | Write-Output
