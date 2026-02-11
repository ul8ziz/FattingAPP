# 03_dll_integrity.ps1 - Check expected DLLs; capture path, size, version, SHA256, PE arch. AppBitness from EXE PE header.
# Do NOT modify or delete any system DLL. Output: logs\diagnostics\dlls.json

param(
    [Parameter(Mandatory=$false)]
    [string]$AppOutputDir = $null,
    [string]$LogsDiagnosticsDir = $null,
    [switch]$JsonOnly = $false,
    [string]$HiProDir = "${env:ProgramFiles(x86)}\HI-PRO",
    [string]$CtkDir = "${env:ProgramFiles(x86)}\Common Files\SignaKlara\CTK",
    [string]$CtkCommDir = "${env:ProgramFiles(x86)}\Common Files\SignaKlara\CTK\communication_modules",
    [string]$System32 = $env:SystemRoot + '\System32',
    [string]$SysWOW64 = $env:SystemRoot + '\SysWOW64'
)

$ErrorActionPreference = 'Continue'

# DLLs of interest: ftd2xx, FTD2XX_NET, FTChipID, HiProWrapper, HI-PRO (CTK), sdnet, SDCOM
$expectedDlls = @('ftd2xx.dll', 'FTD2XX_NET.dll', 'FTChipID.dll', 'HiProWrapper.dll', 'HI-PRO.dll', 'sdnet.dll', 'SDCOM.dll')

# Locations to scan (path -> label). Always include explicit HI-PRO path so HiProWrapper.dll is found.
$hiProLiteral = 'C:\Program Files (x86)\HI-PRO'
$locations = @{}
if ($AppOutputDir -and (Test-Path -LiteralPath $AppOutputDir)) {
    $locations[[System.IO.Path]::GetFullPath($AppOutputDir)] = 'AppOutput'
}
$hiProDirResolved = $null
foreach ($candidate in @($HiProDir, $hiProLiteral)) {
    if (-not $candidate) { continue }
    if (Test-Path -LiteralPath $candidate -PathType Container) {
        $resolved = [System.IO.Path]::GetFullPath($candidate)
        if (-not $locations.ContainsKey($resolved)) {
            $locations[$resolved] = 'HI-PRO'
            $hiProDirResolved = $resolved
        }
        break
    }
}
if (-not $hiProDirResolved -and (Test-Path -LiteralPath $hiProLiteral -PathType Container)) {
    $hiProDirResolved = [System.IO.Path]::GetFullPath($hiProLiteral)
    $locations[$hiProDirResolved] = 'HI-PRO'
}
if (Test-Path -LiteralPath $CtkDir) {
    $locations[$CtkDir] = 'CTK'
}
if (Test-Path -LiteralPath $CtkCommDir) {
    $locations[$CtkCommDir] = 'CTK\communication_modules'
}
$locations[$SysWOW64] = 'SysWOW64'
$locations[$System32] = 'System32'

# Read PE machine type (lightweight: first 2 bytes of PE header)
function Get-PeArchitecture {
    param([string]$FilePath)
    try {
        $bytes = [System.IO.File]::ReadAllBytes($FilePath)
        # DOS header: e_lfanew at offset 0x3C (60)
        if ($bytes.Length -lt 64) { return 'Unknown' }
        $e_lfanew = [BitConverter]::ToInt32($bytes, 60)
        if ($e_lfanew -lt 0 -or $e_lfanew -ge $bytes.Length - 6) { return 'Unknown' }
        # PE signature at e_lfanew; Machine at e_lfanew+4 (2 bytes)
        $machine = [BitConverter]::ToUInt16($bytes, $e_lfanew + 4)
        switch ($machine) {
            0x014c { return 'x86' }
            0x8664 { return 'x64' }
            0x0200 { return 'IA64' }
            default { return "Unknown(0x$($machine.ToString('X4')))" }
        }
    } catch {
        return 'Error'
    }
}

$report = [System.Collections.ArrayList]::new()
$foundPaths = @{}

foreach ($basePath in $locations.Keys) {
    $label = $locations[$basePath]
    foreach ($dll in $expectedDlls) {
        $fullPath = Join-Path -Path $basePath -ChildPath $dll
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
        try {
            $fi = Get-Item -LiteralPath $fullPath -ErrorAction Stop
            $size = $fi.Length
            $lastWrite = $fi.LastWriteTimeUtc.ToString('o')
            $ver = $null
            $prodVer = $null
            try {
                $verInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($fullPath)
                $ver = $verInfo.FileVersion
                $prodVer = $verInfo.ProductVersion
            } catch { }
            $hash = $null
            try {
                $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256 -ErrorAction SilentlyContinue).Hash
            } catch { }
            $arch = Get-PeArchitecture -FilePath $fullPath
            $o = [PSCustomObject]@{
                FullPath      = $fullPath
                LocationLabel = $label
                FileName      = $dll
                Size          = $size
                LastWriteTime = $lastWrite
                FileVersion   = $ver
                ProductVersion = $prodVer
                SHA256        = $hash
                PEArchitecture = $arch
            }
            [void]$report.Add($o)
            $foundPaths[$dll] = $true
        } catch {
            # skip
        }
    }
}

# App bitness: read from built app EXE PE header in AppOutputDir (NOT PowerShell process)
$appBitness = 'Unknown'
$appExePath = $null
if ($AppOutputDir -and (Test-Path -LiteralPath $AppOutputDir)) {
    $appDirFull = [System.IO.Path]::GetFullPath($AppOutputDir)
    # Prefer: Ul8ziz.FittingApp.App.exe; fallback: first *.exe matching project name or any .exe
    $preferredName = 'Ul8ziz.FittingApp.App.exe'
    $preferredPath = Join-Path -Path $appDirFull -ChildPath $preferredName
    if (Test-Path -LiteralPath $preferredPath -PathType Leaf) {
        $appExePath = $preferredPath
        $appBitness = Get-PeArchitecture -FilePath $appExePath
    }
    if (-not $appExePath) {
        $exes = Get-ChildItem -LiteralPath $appDirFull -Filter '*.exe' -File -ErrorAction SilentlyContinue
        foreach ($exe in $exes) {
            $appExePath = $exe.FullName
            $appBitness = Get-PeArchitecture -FilePath $appExePath
            if ($appBitness -eq 'x86' -or $appBitness -eq 'x64') { break }
        }
    }
}
$powerShellBitness = if ([Environment]::Is64BitProcess) { 'x64' } else { 'x86' }

# Validation: warn if AppBitness (EXE) mismatches vendor DLLs (typically x86)
$warnings = [System.Collections.ArrayList]::new()
if ($appBitness -eq 'x64') {
    [void]$warnings.Add("App EXE is x64; HI-PRO/CTK vendor DLLs are typically x86. Prefer x86 build for HI-PRO.")
}
$hiproWrapper = $report | Where-Object { $_.FileName -eq 'HiProWrapper.dll' }
if (-not $hiproWrapper -or $hiproWrapper.Count -eq 0) {
    if ($hiProDirResolved) {
        [void]$warnings.Add("HiProWrapper.dll not found under HI-PRO ($hiProDirResolved). Expected at: $hiProDirResolved\HiProWrapper.dll")
    } else {
        [void]$warnings.Add("HiProWrapper.dll not found. HI-PRO folder not present or not scanned (check C:\Program Files (x86)\HI-PRO).")
    }
}
foreach ($r in $report) {
    if ($r.PEArchitecture -eq 'x86' -and $appBitness -eq 'x64') {
        [void]$warnings.Add("x86 DLL at $($r.FullPath) vs x64 app EXE may cause load failures.")
    }
}

$out = [PSCustomObject]@{
    AppBitness        = $appBitness
    AppExePath        = $appExePath
    PowerShellBitness = $powerShellBitness
    DllEntries        = @($report)
    Warnings          = @($warnings)
}
$dllJsonOut = $out | ConvertTo-Json -Depth 5 -Compress

$repoRoot = Join-Path $PSScriptRoot '..\..'
$outDir = $LogsDiagnosticsDir
if (-not $outDir) { $outDir = Join-Path (Join-Path $repoRoot 'logs') 'diagnostics' }
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$dllsPath = Join-Path $outDir 'dlls.json'
Set-Content -Path $dllsPath -Value $dllJsonOut -Encoding UTF8 -ErrorAction SilentlyContinue

if ($JsonOnly) {
    Write-Output $dllJsonOut
    return
}
Write-Host "========== DLL INTEGRITY ==========" -ForegroundColor Cyan
Write-Host "AppBitness (from EXE PE): $appBitness | AppExePath: $appExePath | PowerShellBitness: $powerShellBitness" -ForegroundColor Gray
$report | Format-Table -AutoSize LocationLabel, FileName, Size, PEArchitecture, FileVersion -Wrap | Out-String | Write-Host
Write-Host "Details (path, hash, version):" -ForegroundColor Gray
foreach ($r in $report) {
    Write-Host "  $($r.FullPath)"
    Write-Host "    Size: $($r.Size) | PE: $($r.PEArchitecture) | FileVersion: $($r.FileVersion) | SHA256: $($r.SHA256)"
}
if ($warnings.Count -gt 0) {
    Write-Host "Warnings:" -ForegroundColor Yellow
    $warnings | ForEach-Object { Write-Host "  $_" }
}
Write-Host "Written: $dllsPath" -ForegroundColor Gray
Write-Output $dllJsonOut
