<#
.SYNOPSIS
    Registers ZenArchive context menu entries in Windows Explorer.
.DESCRIPTION
    Adds "Extract Here", "Extract Files...", "Extract to <ArchiveName>\", and "Add to ZenArchive..."
    to the right-click context menu for archive file types (.zip, .7z, .rar, .tar, .gz).
    Also enables the classic full context menu on Windows 11 (bypassing "Show more options").
    
    Run as Administrator for HKLM registration, or as user for HKCU registration.
.PARAMETER AppPath
    Full path to the ZenArchive executable. If not specified, auto-detects from script location.
.PARAMETER Unregister
    If specified, removes all ZenArchive context menu entries.
#>

param(
    [string]$AppPath,
    [switch]$Unregister
)

$ErrorActionPreference = "Stop"

# Auto-detect app path
if ([string]::IsNullOrEmpty($AppPath)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectDir = Split-Path -Parent $scriptDir
    $publishDir = Join-Path $projectDir "bin\Release\net9.0-windows\win-x64\publish"
    $debugDir = Join-Path $projectDir "bin\Debug\net9.0-windows"
    
    if (Test-Path (Join-Path $publishDir "Archieve App.exe")) {
        $AppPath = Join-Path $publishDir "Archieve App.exe"
    } elseif (Test-Path (Join-Path $debugDir "Archieve App.exe")) {
        $AppPath = Join-Path $debugDir "Archieve App.exe"
    } else {
        # Check Program Files install location
        $pfPath = "C:\Program Files\ZenArchive\Archieve App.exe"
        if (Test-Path $pfPath) {
            $AppPath = $pfPath
        } else {
            Write-Error "Cannot find Archieve App.exe. Please specify -AppPath parameter."
            exit 1
        }
    }
}

$AppPath = (Resolve-Path $AppPath).Path
Write-Host "ZenArchive executable: $AppPath" -ForegroundColor Cyan

# Determine if we have admin privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$rootKey = if ($isAdmin) { "HKLM:" } else { "HKCU:" }
Write-Host "Registry root: $(if ($isAdmin) {'HKLM (system-wide)'} else {'HKCU (current user)'})" -ForegroundColor Yellow

$extensions = @(".zip", ".7z", ".rar", ".tar", ".gz", ".tar.gz", ".tgz", ".bz2", ".xz")
$appliesTo = ($extensions | ForEach-Object { "System.FileExtension:=$_" }) -join " OR "
$quotedApp = """$AppPath"""

function Register-ContextMenuEntries {
    Write-Host "`n[1/4] Registering file type associations..." -ForegroundColor Green
    
    # ZenArchive.Archive ProgID
    $progIdBase = "$rootKey\Software\Classes\ZenArchive.Archive"
    New-Item -Path "$progIdBase" -Force | Out-Null
    Set-ItemProperty -Path "$progIdBase" -Name "(Default)" -Value "ZenArchive Compressed Archive"
    
    New-Item -Path "$progIdBase\DefaultIcon" -Force | Out-Null
    Set-ItemProperty -Path "$progIdBase\DefaultIcon" -Name "(Default)" -Value "$AppPath,0"
    
    New-Item -Path "$progIdBase\shell\open\command" -Force | Out-Null
    Set-ItemProperty -Path "$progIdBase\shell\open\command" -Name "(Default)" -Value "$quotedApp ""%1"""
    
    foreach ($ext in $extensions) {
        $extKey = "$rootKey\Software\Classes\$ext"
        # Don't override if already set, just add OpenWithProgids
        $owKey = "$extKey\OpenWithProgids"
        New-Item -Path $owKey -Force | Out-Null
        Set-ItemProperty -Path $owKey -Name "ZenArchive.Archive" -Value "" -Type String
    }
    
    Write-Host "  -> File associations registered" -ForegroundColor Green
    
    Write-Host "`n[2/4] Registering Explorer context menu entries..." -ForegroundColor Green
    
    # Wildcard shell entries (appear on all files matching AppliesTo)
    $starBase = "$rootKey\Software\Classes\*\shell"
    
    # Extract Here
    $ehKey = "$starBase\ZenArchiveExtractHere"
    New-Item -Path "$ehKey\command" -Force | Out-Null
    Set-ItemProperty -Path $ehKey -Name "(Default)" -Value "Extract Here"
    Set-ItemProperty -Path $ehKey -Name "Icon" -Value $quotedApp
    Set-ItemProperty -Path $ehKey -Name "AppliesTo" -Value $appliesTo
    Set-ItemProperty -Path "$ehKey\command" -Name "(Default)" -Value "$quotedApp -extract-here ""%1"""
    
    # Extract Files...
    $efKey = "$starBase\ZenArchiveExtractFiles"
    New-Item -Path "$efKey\command" -Force | Out-Null
    Set-ItemProperty -Path $efKey -Name "(Default)" -Value "Extract Files..."
    Set-ItemProperty -Path $efKey -Name "Icon" -Value $quotedApp
    Set-ItemProperty -Path $efKey -Name "AppliesTo" -Value $appliesTo
    Set-ItemProperty -Path "$efKey\command" -Name "(Default)" -Value "$quotedApp -extract-files ""%1"""
    
    # Extract to <ArchiveName>\
    $seKey = "$starBase\ZenArchiveSmartExtract"
    New-Item -Path "$seKey\command" -Force | Out-Null
    Set-ItemProperty -Path $seKey -Name "(Default)" -Value "Extract to <ArchiveName>\\"
    Set-ItemProperty -Path $seKey -Name "Icon" -Value $quotedApp
    Set-ItemProperty -Path $seKey -Name "AppliesTo" -Value $appliesTo
    Set-ItemProperty -Path "$seKey\command" -Name "(Default)" -Value "$quotedApp -smart-extract ""%1"""
    
    # Add to ZenArchive... (for any file)
    $acKey = "$starBase\ZenArchiveCompress"
    New-Item -Path "$acKey\command" -Force | Out-Null
    Set-ItemProperty -Path $acKey -Name "(Default)" -Value "Add to ZenArchive..."
    Set-ItemProperty -Path $acKey -Name "Icon" -Value $quotedApp
    Set-ItemProperty -Path "$acKey\command" -Name "(Default)" -Value "$quotedApp -compress ""%1"""
    
    # Directory context menu (compress folder)
    $dirKey = "$rootKey\Software\Classes\Directory\shell\ZenArchiveCompress"
    New-Item -Path "$dirKey\command" -Force | Out-Null
    Set-ItemProperty -Path $dirKey -Name "(Default)" -Value "Add to ZenArchive..."
    Set-ItemProperty -Path $dirKey -Name "Icon" -Value $quotedApp
    Set-ItemProperty -Path "$dirKey\command" -Name "(Default)" -Value "$quotedApp -compress ""%1"""
    
    Write-Host "  -> Context menu entries registered" -ForegroundColor Green
    
    Write-Host "`n[3/4] Registering SystemFileAssociations..." -ForegroundColor Green
    
    foreach ($ext in @(".zip", ".7z", ".rar", ".tar", ".gz")) {
        $sfaBase = "$rootKey\Software\Classes\SystemFileAssociations\$ext\shell"
        
        # Open
        New-Item -Path "$sfaBase\ZenArchiveOpen\command" -Force | Out-Null
        Set-ItemProperty -Path "$sfaBase\ZenArchiveOpen" -Name "(Default)" -Value "Open with ZenArchive"
        Set-ItemProperty -Path "$sfaBase\ZenArchiveOpen" -Name "Icon" -Value $quotedApp
        Set-ItemProperty -Path "$sfaBase\ZenArchiveOpen\command" -Name "(Default)" -Value "$quotedApp ""%1"""
        
        # Extract Here
        New-Item -Path "$sfaBase\ZenArchiveExtractHere\command" -Force | Out-Null
        Set-ItemProperty -Path "$sfaBase\ZenArchiveExtractHere" -Name "(Default)" -Value "Extract Here"
        Set-ItemProperty -Path "$sfaBase\ZenArchiveExtractHere" -Name "Icon" -Value $quotedApp
        Set-ItemProperty -Path "$sfaBase\ZenArchiveExtractHere\command" -Name "(Default)" -Value "$quotedApp -extract-here ""%1"""
        
        # Extract Files...
        New-Item -Path "$sfaBase\ZenArchiveExtractFiles\command" -Force | Out-Null
        Set-ItemProperty -Path "$sfaBase\ZenArchiveExtractFiles" -Name "(Default)" -Value "Extract Files..."
        Set-ItemProperty -Path "$sfaBase\ZenArchiveExtractFiles" -Name "Icon" -Value $quotedApp
        Set-ItemProperty -Path "$sfaBase\ZenArchiveExtractFiles\command" -Name "(Default)" -Value "$quotedApp -extract-files ""%1"""
        
        # Smart Extract
        New-Item -Path "$sfaBase\ZenArchiveSmartExtract\command" -Force | Out-Null
        Set-ItemProperty -Path "$sfaBase\ZenArchiveSmartExtract" -Name "(Default)" -Value "Extract to <ArchiveName>\\"
        Set-ItemProperty -Path "$sfaBase\ZenArchiveSmartExtract" -Name "Icon" -Value $quotedApp
        Set-ItemProperty -Path "$sfaBase\ZenArchiveSmartExtract\command" -Name "(Default)" -Value "$quotedApp -smart-extract ""%1"""
    }
    
    Write-Host "  -> SystemFileAssociations registered" -ForegroundColor Green
    
    Write-Host "`n[4/4] Enabling Windows 11 classic context menu..." -ForegroundColor Green
    
    # Windows 11 classic context menu CLSID (bypasses "Show more options")
    $clsidKey = "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32"
    New-Item -Path $clsidKey -Force | Out-Null
    Set-ItemProperty -Path $clsidKey -Name "(Default)" -Value ""
    
    Write-Host "  -> Windows 11 classic context menu enabled" -ForegroundColor Green
    
    # Restart Explorer to apply changes
    Write-Host "`nRestarting Explorer to apply changes..." -ForegroundColor Yellow
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process explorer.exe
    
    Write-Host "`n===========================================================" -ForegroundColor Green
    Write-Host "  ZenArchive context menu registered successfully!" -ForegroundColor Green
    Write-Host "  Right-click any .zip, .7z, .rar, .tar, .gz file to see" -ForegroundColor White
    Write-Host "  ZenArchive options in the context menu." -ForegroundColor White
    Write-Host "===========================================================" -ForegroundColor Green
}

function Unregister-ContextMenuEntries {
    Write-Host "`nRemoving ZenArchive context menu entries..." -ForegroundColor Yellow
    
    $keysToRemove = @(
        "$rootKey\Software\Classes\ZenArchive.Archive",
        "$rootKey\Software\Classes\*\shell\ZenArchiveExtractHere",
        "$rootKey\Software\Classes\*\shell\ZenArchiveExtractFiles",
        "$rootKey\Software\Classes\*\shell\ZenArchiveSmartExtract",
        "$rootKey\Software\Classes\*\shell\ZenArchiveCompress",
        "$rootKey\Software\Classes\Directory\shell\ZenArchiveCompress",
        "$rootKey\Software\Classes\CompressedFolder\shell\ZenArchiveOpen",
        "$rootKey\Software\Classes\CompressedFolder\shell\ZenArchiveExtractHere",
        "$rootKey\Software\Classes\CompressedFolder\shell\ZenArchiveExtractFiles",
        "$rootKey\Software\Classes\CompressedFolder\shell\ZenArchiveSmartExtract"
    )
    
    foreach ($ext in @(".zip", ".7z", ".rar", ".tar", ".gz")) {
        $keysToRemove += "$rootKey\Software\Classes\SystemFileAssociations\$ext\shell\ZenArchiveOpen"
        $keysToRemove += "$rootKey\Software\Classes\SystemFileAssociations\$ext\shell\ZenArchiveExtractHere"
        $keysToRemove += "$rootKey\Software\Classes\SystemFileAssociations\$ext\shell\ZenArchiveExtractFiles"
        $keysToRemove += "$rootKey\Software\Classes\SystemFileAssociations\$ext\shell\ZenArchiveSmartExtract"
    }
    
    foreach ($key in $keysToRemove) {
        if (Test-Path $key) {
            Remove-Item -Path $key -Recurse -Force
            Write-Host "  Removed: $key" -ForegroundColor Gray
        }
    }
    
    # Remove Win11 CLSID override
    $clsidKey = "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"
    if (Test-Path $clsidKey) {
        Remove-Item -Path $clsidKey -Recurse -Force
        Write-Host "  Removed Windows 11 classic context menu override" -ForegroundColor Gray
    }
    
    Write-Host "`n  ZenArchive context menu entries removed." -ForegroundColor Green
    
    # Restart Explorer
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process explorer.exe
}

if ($Unregister) {
    Unregister-ContextMenuEntries
} else {
    Register-ContextMenuEntries
}
