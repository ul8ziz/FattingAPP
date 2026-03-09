# Creates AppIcon.ico from AppIcon.png for the .exe and shortcuts.
# Run from repo root: .\scripts\create-app-icon.ps1
# For best compatibility (incl. Inno Setup), install ImageMagick and run: magick convert src\App\Resources\Images\AppIcon.png -define icon:auto-resize=256,48,32,16 src\App\Resources\Images\AppIcon.ico

$ErrorActionPreference = "Stop"
$repoRoot = Join-Path $PSScriptRoot ".."
$pngPath = Join-Path $repoRoot "src\App\Resources\Images\AppIcon.png"
$icoPath = Join-Path $repoRoot "src\App\Resources\Images\AppIcon.ico"

if (-not (Test-Path $pngPath)) {
    Write-Error "AppIcon.png not found: $pngPath"
}

# 1) Prefer ImageMagick — produces standard multi-size .ico (works with MSBuild and Inno Setup)
$magick = Get-Command magick -ErrorAction SilentlyContinue
if ($magick) {
    Write-Host "Using ImageMagick..." -ForegroundColor Cyan
    & magick convert (Resolve-Path $pngPath) -define icon:auto-resize=256,128,64,48,32,16 $icoPath
    if ($LASTEXITCODE -eq 0 -and (Test-Path $icoPath)) {
        Write-Host "Created: $icoPath" -ForegroundColor Green
        exit 0
    }
}

# 2) Fallback: .NET — resize to 48x48, then Icon.FromHandle + Save (format accepted by MSBuild for ApplicationIcon)
Write-Host "Using .NET (48x48)..." -ForegroundColor Cyan
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $pngPath))
$size = 48
$bmp = New-Object System.Drawing.Bitmap($size, $size)
try {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($src, 0, 0, $size, $size)
    } finally {
        $g.Dispose()
    }
    $src.Dispose()
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    try {
        $fs = [System.IO.File]::Create($icoPath)
        $icon.Save($fs)
        $fs.Close()
    } finally {
        $icon.Dispose()
    }
} finally {
    $bmp.Dispose()
}

if (Test-Path $icoPath) {
    Write-Host "Created: $icoPath" -ForegroundColor Green
    Write-Host "Tip: For Inno Setup icon, install ImageMagick and re-run this script." -ForegroundColor Yellow
} else {
    Write-Error "Failed to create $icoPath"
}
