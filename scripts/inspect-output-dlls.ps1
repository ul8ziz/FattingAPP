# inspect-output-dlls.ps1 - Inventory DLLs in app output folder; report path, size, date, PE architecture (x86/x64).
# Usage: powershell -ExecutionPolicy Bypass -File scripts\inspect-output-dlls.ps1 [-OutputDir "src\App\bin\Debug\net10.0-windows"]

param([string]$OutputDir)

if (-not $OutputDir) {
    $OutputDir = "src\App\bin\Debug\net10.0-windows"
    $base = $PSScriptRoot
    if ($base) { $OutputDir = Join-Path (Split-Path $base -Parent) $OutputDir }
    else { $OutputDir = Join-Path (Get-Location) $OutputDir }
}
if (-not (Test-Path -LiteralPath $OutputDir)) {
    Write-Host "Output dir not found: $OutputDir"
    exit 1
}

$dllNames = @("sdnet.dll","SDCOM.dll","FTChipID.dll","ftd2xx.dll","FTD2XX_NET.dll","HiProWrapper.dll",
    "SoundDesigner.Core.dll","SoundDesigner.Framework.dll","SoundDesigner.Manufacturing.dll","SoundDesigner.Modules.dll")
$report = @()
$report += "=== DLL inventory: $OutputDir ==="
$report += ""

function Get-PEArchitecture {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return "N/A" }
    try {
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        if ($bytes.Length -lt 64) { return "N/A" }
        $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
        if ($peOffset -lt 0 -or $peOffset -ge $bytes.Length - 6) { return "N/A" }
        $machine = [BitConverter]::ToInt16($bytes, $peOffset + 4)
        if ($machine -eq 0x8664) { return "x64" }
        if ($machine -eq 0x14c) { return "x86" }
        return "Other($machine)"
    } catch { return "Error" }
}

foreach ($name in $dllNames) {
    $fullPath = Join-Path $OutputDir $name
    $exists = Test-Path -LiteralPath $fullPath
    $size = $null; $date = $null; $arch = "N/A"
    if ($exists) {
        $item = Get-Item -LiteralPath $fullPath
        $size = $item.Length
        $date = $item.LastWriteTime.ToString("o")
        $arch = Get-PEArchitecture -Path $fullPath
    }
    $report += "File=$name | Path=$fullPath | Exists=$exists | Size=$size | LastWrite=$date | CPU=$arch"
}
$report += ""

# Any DLL in folder containing CTK/SignaKlara/HI-PRO in name
Get-ChildItem -Path $OutputDir -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Name -match "CTK|Signa|Klara|HI-PRO|HiPro|ftd|FTChip|SDCOM|sdnet") {
        $arch = Get-PEArchitecture -Path $_.FullName
        $report += "Extra: $($_.Name) | Size=$($_.Length) | LastWrite=$($_.LastWriteTime.ToString('o')) | CPU=$arch"
    }
}
$report += "=== end inventory ==="
$report -join "`r`n" | Write-Output
