<#
.SYNOPSIS
    Builds and packages ZenArchieve Windows 64-bit Installer.
.DESCRIPTION
    1. Compiles and publishes self-contained .NET 9 win-x64 binaries.
    2. Compiles Inno Setup script (ZenArchieve_Setup.iss) into ZenArchieve_Setup_v2.1.exe.
#>

param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $false
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir
$projectFile = Join-Path $projectDir "Archieve App.csproj"
$issFile = Join-Path $scriptDir "ZenArchieve_Setup.iss"
$outputDir = Join-Path $projectDir "bin\installer"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   ZenArchieve Installer Builder (.NET 9 Win-x64)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Step 1: Clean & Publish .NET 9 Binaries
$publishDir = Join-Path $projectDir "bin\$Configuration\net9.0-windows\$Runtime\publish"
if (Test-Path $publishDir) {
    Remove-Item "$publishDir\*" -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n[1/2] Publishing application binaries ($Configuration - $Runtime, SelfContained=$SelfContained)..." -ForegroundColor Yellow
$publishArgs = @(
    "publish",
    "`"$projectFile`"",
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "$SelfContained",
    "-p:PublishSingleFile=false",
    "-p:DebugType=none",
    "-p:DebugSymbols=false"
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# Ensure tools are copied into publish folder
$toolsSrc = Join-Path $projectDir "tools"
$toolsDest = Join-Path $publishDir "tools"
if (Test-Path $toolsSrc) {
    if (-not (Test-Path $toolsDest)) { New-Item -ItemType Directory -Path $toolsDest -Force | Out-Null }
    Copy-Item "$toolsSrc\*" -Destination $toolsDest -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $publishDir)) {
    Write-Error "Publish directory not found at: $publishDir"
    exit 1
}
Write-Host "  -> Successfully published to: $publishDir" -ForegroundColor Green

# Step 2: Locate Inno Setup Compiler (ISCC.exe)
Write-Host "`n[2/2] Locating Inno Setup 6 compiler (ISCC.exe)..." -ForegroundColor Yellow
$potentialIsccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\ProgramData\chocolatey\bin\iscc.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$isccPath = $null
$cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($cmd) {
    $isccPath = $cmd.Source
} else {
    foreach ($p in $potentialIsccPaths) {
        if (Test-Path $p) {
            $isccPath = $p
            break
        }
    }
}

if ($isccPath) {
    Write-Host "  -> Found ISCC at: $isccPath" -ForegroundColor Green
    Write-Host "  -> Compiling Inno Setup script: $issFile..." -ForegroundColor Cyan

    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    & "$isccPath" "$issFile"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n==========================================================" -ForegroundColor Green
        Write-Host " [SUCCESS] Installer generated successfully in:" -ForegroundColor Green
        Write-Host " $outputDir" -ForegroundColor White
        Get-ChildItem $outputDir -Filter "*.exe" | ForEach-Object {
            Write-Host "   -> $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Yellow
        }
        $distDir = Join-Path (Split-Path -Parent $projectDir) "installer_dist"
        if (Test-Path $distDir) {
            Copy-Item "$outputDir\ZenArchieve_Setup_v2.1.2.exe" -Destination $distDir -Force -ErrorAction SilentlyContinue
        }
        Write-Host "==========================================================" -ForegroundColor Green
    } else {
        Write-Error "ISCC compilation failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
} else {
    Write-Host "`n[INFO] Inno Setup compiler (ISCC.exe) not found in standard paths." -ForegroundColor Yellow
    Write-Host "  -> The self-contained application binaries are published and ready." -ForegroundColor White
    Write-Host "  -> To build the installer exe, install Inno Setup 6:" -ForegroundColor White
    Write-Host "     winget install JRSoftware.InnoSetup" -ForegroundColor Cyan
    Write-Host "     or download from: https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
    Write-Host "  -> Then re-run this script or compile: $issFile" -ForegroundColor White
}
