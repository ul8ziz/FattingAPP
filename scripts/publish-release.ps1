# Builds a runnable, self-contained release and optionally packs it for distribution.
# Run from repo root: .\scripts\publish-release.ps1
# Options:
#   -CreateZip      Create publish\FittingApp-Release.zip from the publish folder
#   -BuildInstaller Build a conventional Windows setup.exe (downloads Inno Setup if needed)
#   -OpenFolder     Open the publish folder in Explorer when done

param(
    [switch]$CreateZip,
    [switch]$BuildInstaller,
    [switch]$OpenFolder
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot + "\.."
$appProj = Join-Path $repoRoot "src\App\Ul8ziz.FittingApp.App.csproj"
$publishDir = Join-Path $repoRoot "publish\FittingApp"
$profile = "Release-win-x86"
$toolsDir = Join-Path $repoRoot "tools"
$innoDir = Join-Path $toolsDir "InnoSetup"

function Find-ISCC {
    $paths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        (Join-Path $innoDir "ISCC.exe")
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

function Install-InnoSetupPortable {
    $innoUrl = "https://jrsoftware.org/download.php/is.exe?site=1"
    $exePath = Join-Path $toolsDir "innosetup-installer.exe"
    if (-not (Test-Path $toolsDir)) { New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null }
    Write-Host "Downloading Inno Setup (one-time)..." -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri $innoUrl -OutFile $exePath -UseBasicParsing
    } catch {
        Write-Warning "Download failed: $_"
        return $false
    }
    Write-Host "Installing Inno Setup to $innoDir ..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $exePath -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$innoDir" -Wait -PassThru
    try { Remove-Item $exePath -Force -ErrorAction SilentlyContinue } catch {}
    if ($proc.ExitCode -ne 0) {
        Write-Warning "Inno Setup install failed (exit code $($proc.ExitCode)). Install manually from https://jrsoftware.org/isinfo.php"
        return $false
    }
    return (Test-Path (Join-Path $innoDir "ISCC.exe"))
}

if (-not (Test-Path $appProj)) {
    Write-Error "App project not found: $appProj"
}

Write-Host "Publishing Fitting App (Release, win-x86, self-contained)..." -ForegroundColor Cyan
Push-Location $repoRoot

try {
    dotnet publish $appProj -p:Configuration=Release -p:Platform=x86 /p:PublishProfile=$profile
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    $exePath = Join-Path $publishDir "Ul8ziz.FittingApp.App.exe"
    if (-not (Test-Path $exePath)) {
        Write-Error "Expected executable not found: $exePath"
    }

    Write-Host "Publish succeeded: $publishDir" -ForegroundColor Green

    if ($CreateZip) {
        $zipPath = Join-Path $repoRoot "publish\FittingApp-Release.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "Zip created: $zipPath" -ForegroundColor Green
    }

    if ($BuildInstaller) {
        $iscc = Find-ISCC
        if (-not $iscc) { Install-InnoSetupPortable | Out-Null; $iscc = Find-ISCC }
        if ($iscc) {
            $iss = Join-Path $repoRoot "installer\FittingApp.iss"
            & $iscc $iss
            if ($LASTEXITCODE -eq 0) { Write-Host "Installer built: publish\FittingApp-Setup-1.0.0.exe" -ForegroundColor Green }
            else { Write-Warning "Inno Setup compilation failed (exit code $LASTEXITCODE)." }
        } else {
            Write-Warning "Inno Setup (ISCC.exe) not found. Install from https://jrsoftware.org/isinfo.php and run: .\scripts\publish-release.ps1 -BuildInstaller"
        }
    }

    if ($OpenFolder) {
        Start-Process explorer.exe -ArgumentList "/select,`"$exePath`""
    }
}
finally {
    Pop-Location
}

Write-Host "Done. Run the app from: $exePath" -ForegroundColor Cyan
