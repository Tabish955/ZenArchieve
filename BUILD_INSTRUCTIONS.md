# ZenArchieve — Build & Packaging Instructions (v2.2.0)

This guide documents the complete end-to-end process to build, package, test, release, and submit **ZenArchieve v2.2.0** on Windows 10/11 (x64).

---

## 🛠️ Prerequisites

Ensure the following tools are installed on your Windows machine:

1. **.NET 9.0 SDK & Desktop Runtime** (x64):
   ```powershell
   winget install Microsoft.DotNet.SDK.9
   ```
   Verify with `dotnet --version` (should show `9.0.x`).

2. **Inno Setup 6** (for building the `.exe` setup installer):
   ```powershell
   winget install JRSoftware.InnoSetup
   ```
   Default installation path: `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`

3. **Windows 10/11 SDK or BuildTools** (for `makeappx.exe` to package `.msix`):
   ```powershell
   winget install Microsoft.WindowsSDK.10.0.26100
   ```
   Or via NuGet package `Microsoft.Windows.SDK.BuildTools`.

4. **Python 3.10+** (for MSIX orchestration script):
   ```powershell
   winget install Python.Python.3.12
   ```

5. **GitHub CLI (`gh`)** (for creating GitHub releases and uploading assets):
   ```powershell
   winget install GitHub.cli
   gh auth login
   ```

---

## 🚀 Step-by-Step Build Pipeline

All commands should be executed from the repository root directory (`d:\Archieve` or your clone directory).

### 1. Compile the Release Build
Restore packages and verify that the application compiles with zero errors and zero warnings:
```powershell
dotnet build "Archieve App/Archieve App.csproj" -c Release
```

### 2. Framework-Dependent Publish
Publish the application binaries for 64-bit Windows without debug symbols:
```powershell
dotnet publish "Archieve App/Archieve App.csproj" -c Release -r win-x64 --self-contained false -p:DebugType=none -p:DebugSymbols=false
```
Published output is staged at:
`Archieve App\bin\Release\net9.0-windows\win-x64\publish\`

### 3. Build the Inno Setup Installer (`.exe`)
Run the automated installer build script. It verifies published binaries, runs `ISCC.exe` on `ZenArchieve_Setup.iss`, and automatically copies the installer to `installer_dist\`:
```powershell
powershell -ExecutionPolicy Bypass -File "Archieve App/installer/build_installer.ps1"
```
Output:
`installer_dist\ZenArchieve_Setup_v2.2.0.exe` (~6.7 MB)

### 4. Build the Microsoft Store MSIX Package (`.msix`)
Run the automated MSIX packager. It creates the packaging layout, injects `AppxManifest.xml` (Identity Version `2.2.0.0`), executes `makeappx.exe pack`, and copies the final package to `installer_dist\`:
```powershell
python "Archieve App/installer/build_msix.py"
```
Output:
`installer_dist\ZenArchieve_v2.2.0_x64.msix` (~6.3 MB)

---

## 🔍 Validation & Testing

Verify that both built installers are present and intact:
```powershell
Get-ChildItem -Path "installer_dist" -Filter "*v2.2.0*" | Select-Object Name, Length, LastWriteTime
```

Calculate SHA-256 checksums:
```powershell
Get-FileHash -Algorithm SHA256 "installer_dist\ZenArchieve_Setup_v2.2.0.exe", "installer_dist\ZenArchieve_v2.2.0_x64.msix" | Format-List
```

---

## 📦 Create GitHub Release

Push the tag and publish the release with both installer assets:

```powershell
# 1. Create and push the annotated tag
git tag -a v2.2.0 -m "ZenArchieve v2.2.0"
git push origin v2.2.0

# 2. Publish GitHub Release via GitHub CLI
gh release create v2.2.0 `
    "installer_dist\ZenArchieve_Setup_v2.2.0.exe#ZenArchieve Setup v2.2.0 (EXE)" `
    "installer_dist\ZenArchieve_v2.2.0_x64.msix#ZenArchieve v2.2.0 (MSIX Package)" `
    --title "ZenArchieve v2.2.0 — Security Hardening & Desktop Experience" `
    --notes "### What's New in v2.2.0
* **Fixed**: Dialog/extraction windows no longer have transparent Mica backgrounds (\`WindowBackdropType=\"None\"\` and solid background brush).
* **Fixed**: Smart Extract no longer wraps single loose files in unnecessary folders (only wraps when 2+ loose root files or 2+ root segments).
* **Fixed**: Rating/review dialog now appears after boot as intended (\`--bootup-review\` switch registered in HKCU Run).
* **New**: Lightweight background updater runs at Windows logon (\`--bootup-updater\`), retries every 30s on no internet, and exits cleanly.
* **Security**: Encrypted archives are decrypted in-process (SharpCompress) to avoid 7z.exe CLI password leakage."
```

---

## 🏪 Microsoft Store Submission

1. Sign in to the [Microsoft Partner Center](https://partner.microsoft.com/dashboard).
2. Navigate to **Apps and games** → **ZenArchieve**.
3. Create a new submission.
4. Upload `installer_dist\ZenArchieve_v2.2.0_x64.msix` under **Packages**.
5. Update Store listing description with the v2.2.0 release notes.
6. Submit to certification. See [STORE_SUBMISSION_CHECKLIST.md](file:///d:/Archieve/STORE_SUBMISSION_CHECKLIST.md) for full compliance checks.
