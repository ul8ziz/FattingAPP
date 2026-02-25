# Converts AppIcon.png to AppIcon.ico so the .exe and installer show the project icon.
# Run from repo root: .\scripts\create-app-icon.ps1

$ErrorActionPreference = "Stop"
$pngPath = Join-Path $PSScriptRoot "..\src\App\Resources\Images\AppIcon.png"
$icoPath = Join-Path $PSScriptRoot "..\src\App\Resources\Images\AppIcon.ico"

if (-not (Test-Path $pngPath)) {
    Write-Error "AppIcon.png not found: $pngPath"
}

Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $pngPath))
try {
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    try {
        $fs = [System.IO.File]::Create($icoPath)
        $icon.Save($fs)
        $fs.Close()
        Write-Host "Created: $icoPath" -ForegroundColor Green
    } finally {
        $icon.Dispose()
    }
} finally {
    $bmp.Dispose()
}
