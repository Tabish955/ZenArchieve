# Microsoft Store Submission Checklist — ZenArchieve v2.2.1

This checklist guides the developer through publishing the **ZenArchieve v2.2.1** update to the Microsoft Partner Center.

---

## 📋 Package & Identity Verification

- [x] **Package Version**: Must be `2.2.1.0`
  - In `build_msix.py`: `<Identity Name="FluxCode.ZenArchieve" Publisher="CN=DAD1369C-ABD8-4AAD-9FF7-0BA25EDAAED2" Version="2.2.1.0" ProcessorArchitecture="x64" />`
  - In `Archieve App.csproj`: `<Version>2.2.1</Version>`, `<AssemblyVersion>2.2.1.0</AssemblyVersion>`, `<FileVersion>2.2.1.0</FileVersion>`
- [x] **Target Architecture**: `x64`
- [x] **Target Device Family**: `Windows.Desktop` (`MinVersion="10.0.17763.0"`, `MaxVersionTested="10.0.26100.0"`)
- [x] **Self-Contained Packaging**: Bundles .NET 9 Windows Desktop runtime (`SelfContained=true`, `win-x64`), so no external framework dependency declarations are needed in Partner Center.
- [x] **Declared Capabilities**:
  - `runFullTrust` (`rescap:Capability`) only. ZenArchieve is a classic desktop WPF utility requiring full desktop filesystem access for archive compression and extraction. No additional restricted or privacy-invasive capabilities are declared.
- [x] **Package File Location**:
  - `installer_dist\ZenArchieve_v2.2.1_x64.msix` (~65.7 MB, self-contained)

---

## 🛡️ Windows App Certification Kit (WACK) Test

Before uploading to Partner Center, run WACK locally to guarantee zero validation failures during Store ingestion:

```powershell
# If Windows SDK is installed in standard location:
& "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test `
    -p "d:\Archieve\installer_dist\ZenArchieve_v2.2.1_x64.msix" `
    -reportoutputpath "d:\Archieve\wack_report.xml"
```

Verify that all automated checks pass:
- Package compliance tests (manifest syntax, bundle structure)
- Supported APIs test
- Performance & crash test
- Security tests

---

## 🔒 Store Compliance & Startup Tasks

### 1. Boot-time Task Compliance
- Both startup integrations (`ZenArchieveBootUpdater` and `ZenArchieveReviewPrompt`) register under **`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`** (Current User only).
- **No administrator elevation** or HKLM writes are required at runtime.
- **Background Updater Exit Guarantee**:
  - Runs in background with no window shown.
  - Automatically retries on network failure with an exponential backoff/fixed interval up to ~10 minutes.
  - If up-to-date or if the user dismisses the prompt, it invokes `Shutdown(0)` immediately.
  - No background process lingers or consumes system resources.
- **Opt-out Mechanism**:
  - Selecting "Don't check for updates on startup" sets `AppSettings.DisableAutoUpdateCheck = true` and removes the registry run key immediately.
  - The review prompt offers "Don't show again" which removes `ZenArchieveReviewPrompt` from HKCU Run.

### 2. Privacy Policy Requirement
- Microsoft Store requires a publicly accessible Privacy Policy URL for desktop apps.
- ZenArchieve Privacy Policy URL:
  `https://github.com/Tabish955/ZenArchieve/blob/main/PRIVACY_POLICY.md`
- Data Collection Statement:
  *ZenArchieve does not collect, transmit, sell, or store any personal telemetry, user data, or archive contents. All compression, extraction, and hashing occur entirely locally on the user's machine. Update checks contact the public GitHub Releases API anonymously without transmitting identifiable user information.*

---

## 🌐 Partner Center Step-by-Step Submission

1. **Sign In**: Navigate to [Microsoft Partner Center](https://partner.microsoft.com/dashboard).
2. **Select App**: Select **ZenArchieve** (Product ID: `9N4HCX0KRFNP`).
3. **Start Update**: In the left sidebar under *Application overview*, click **Update** to start a new submission.
4. **Packages**:
   - Drag and drop `installer_dist\ZenArchieve_v2.2.1_x64.msix`.
   - Partner Center will validate package identity, architecture (`x64`), and version (`2.2.1.0`).
   - Remove previous version packages (or let Partner Center replace them).
5. **Properties & Age Ratings**:
   - Confirm categories: *Utilities & Tools* → *File Manager*.
   - Confirm age ratings remain unchanged.
6. **Store Listings**:
   - Update **What's new in this version**:
     ```text
     What's New in ZenArchieve v2.2.1:
     • Fixed: Startup crash on boot/review/updater (AllowsTransparency exception).
     • Improved: Self-contained deployment — no separate .NET 9 runtime required.
     • Fixed: Dialog and extraction windows now render with solid Fluent backgrounds for enhanced clarity.
     • Fixed: Smart Extract logic refined to avoid creating unnecessary nested subfolders for single files.
     • Fixed: Rating and review prompt now properly triggers post-boot when appropriate.
     • New: Lightweight boot-time background updater automatically checks for application updates.
     • Security: Encrypted archives are decrypted in-process to prevent password exposure.
     ```
   - Confirm Privacy Policy URL:
     `https://github.com/Tabish955/ZenArchieve/blob/main/PRIVACY_POLICY.md`
   - Confirm Support Contact:
     `https://github.com/Tabish955/ZenArchieve/issues`
7. **Submission Review**:
   - Review all submission sections (Pricing and availability, Properties, Age ratings, Packages, Store listings).
   - Click **Submit to the Store**.
   - Certification typically completes within 24 to 72 hours.
