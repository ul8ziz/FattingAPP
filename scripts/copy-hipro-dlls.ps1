# sanitize OutputDir (remove quotes and trailing whitespace)
$OutputDir = ($OutputDir ?? "").Trim()
$OutputDir = $OutputDir.Trim('"')          # removes leading/trailing quotes
$OutputDir = $OutputDir -replace '"', ''   # removes any remaining quotes

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir.TrimEnd('\','/'))

# copy-hipro-dlls.ps1 - Copy HI-PRO DLLs to app output dir ONLY if missing (project-only; does not modify system).
# Usage: .\scripts\copy-hipro-dlls.ps1 -OutputDir "path\to\app\output"
# Logs source + destination to logs\diagnostics\copy_local.log and host.

param(
    [Parameter(Mandatory=$true)]
    [string]$OutputDir,
    [string]$HiProSource = 'C:\Program Files (x86)\HI-PRO',
    [switch]$IncludeFtd2xx = $true
)

$ErrorActionPreference = 'Continue'
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir.TrimEnd('\', '/'))
if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
    Write-Warning "Output dir does not exist: $OutputDir"
    exit 0
}

$dllsToCopy = @('HiProWrapper.dll')
if ($IncludeFtd2xx) {
    $dllsToCopy += 'ftd2xx.dll', 'FTD2XX_NET.dll'
}

$logDir = Join-Path $OutputDir 'logs\diagnostics'
if (-not (Test-Path -LiteralPath $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
$logFile = Join-Path $logDir 'copy_local.log'

function Write-Log {
    param([string]$Message)
    $line = "[$(Get-Date -Format 'o')] $Message"
    Write-Host $line
    try {
        Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue
    } catch { }
}

if (-not (Test-Path -LiteralPath $HiProSource -PathType Container)) {
    Write-Log "HI-PRO source not found (skip copy): $HiProSource"
    exit 0
}

$copied = 0
foreach ($dll in $dllsToCopy) {
    $src = Join-Path $HiProSource $dll
    $dest = Join-Path $OutputDir $dll
    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
        Write-Log "Source missing (skip): $src"
        continue
    }
    if (Test-Path -LiteralPath $dest -PathType Leaf) {
        Write-Log "Already present (skip): $dest"
        continue
    }
    try {
        Copy-Item -LiteralPath $src -Destination $dest -Force -ErrorAction Stop
        Write-Log "Copied: $src -> $dest"
        $copied++
    } catch {
        Write-Log "Copy failed: $src -> $dest : $($_.Exception.Message)"
    }
}

if ($copied -eq 0) {
    Write-Log "No DLLs copied (all present or source missing)."
} else {
    Write-Log "CopyLocal: $copied file(s) copied into $OutputDir"
}
