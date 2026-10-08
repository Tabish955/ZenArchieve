import os
import shutil
import subprocess

app_publish_dir = r"D:\Archieve\Archieve App\bin\Release\net9.0-windows\win-x64\publish"
assets_src_dir = r"D:\Archieve\msix_package\Assets"
build_dir = r"D:\Archieve\msix_build"
output_dir = r"D:\Archieve\msix_output"
dist_dir = r"D:\Archieve\installer_dist"
makeappx_exe = r"C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\makeappx.exe"

os.makedirs(build_dir, exist_ok=True)
os.makedirs(output_dir, exist_ok=True)
os.makedirs(dist_dir, exist_ok=True)

# Completely clean build dir
print("[1/5] Cleaning and copying published binaries to build staging...")
if os.path.exists(build_dir):
    shutil.rmtree(build_dir)
os.makedirs(build_dir, exist_ok=True)

for item in os.listdir(app_publish_dir):
    src = os.path.join(app_publish_dir, item)
    dst = os.path.join(build_dir, item)
    if os.path.isdir(src):
        shutil.copytree(src, dst)
    else:
        shutil.copy2(src, dst)

print("[2/5] Copying logo assets...")
assets_dst = os.path.join(build_dir, "Assets")
if os.path.exists(assets_dst):
    shutil.rmtree(assets_dst)
shutil.copytree(assets_src_dir, assets_dst)

print("[3/5] Writing AppxManifest.xml with version 2.1.2.0...")
manifest_content = """<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap2="http://schemas.microsoft.com/appx/manifest/uap/windows10/2"
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
  IgnorableNamespaces="uap uap2 uap3 rescap desktop">

  <Identity
    Name="FluxCode.ZenArchieve"
    Publisher="CN=DAD1369C-ABD8-4AAD-9FF7-0BA25EDAAED2"
    Version="2.1.2.0"
    ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>ZenArchieve</DisplayName>
    <PublisherDisplayName>FluxCode</PublisherDisplayName>
    <Logo>Assets\\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application Id="ZenArchieve"
                 Executable="Archieve App.exe"
                 EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="ZenArchieve"
        Description="ZenArchieve - Modern Windows 11 Archiver"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\\Square150x150Logo.png"
        Square44x44Logo="Assets\\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\\Wide310x150Logo.png" Square310x310Logo="Assets\\Square310x310Logo.png" Square71x71Logo="Assets\\Square71x71Logo.png" />
      </uap:VisualElements>
      <Extensions>
        <uap3:Extension Category="windows.fileTypeAssociation">
          <uap3:FileTypeAssociation Name="zenarchieve.archive">
            <uap:DisplayName>ZenArchieve Archive</uap:DisplayName>
            <uap:Logo>Assets\\Square44x44Logo.png</uap:Logo>
            <uap:SupportedFileTypes>
              <uap:FileType>.zip</uap:FileType>
              <uap:FileType>.7z</uap:FileType>
              <uap:FileType>.rar</uap:FileType>
              <uap:FileType>.tar</uap:FileType>
              <uap:FileType>.gz</uap:FileType>
              <uap:FileType>.bz2</uap:FileType>
              <uap:FileType>.xz</uap:FileType>
              <uap:FileType>.iso</uap:FileType>
              <uap:FileType>.cab</uap:FileType>
            </uap:SupportedFileTypes>
            <uap2:SupportedVerbs>
              <uap3:Verb Id="open" Parameters="&quot;%1&quot;">Open with ZenArchieve</uap3:Verb>
              <uap3:Verb Id="extracthere" Parameters="--extract-here &quot;%1&quot;">Extract Here</uap3:Verb>
              <uap3:Verb Id="extractto" Parameters="--extract-to &quot;%1&quot;">Extract to Folder...</uap3:Verb>
              <uap3:Verb Id="smartextract" Parameters="--smart-extract &quot;%1&quot;">Extract to Archive Folder</uap3:Verb>
            </uap2:SupportedVerbs>
          </uap3:FileTypeAssociation>
        </uap3:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"""

manifest_path = os.path.join(build_dir, "AppxManifest.xml")
with open(manifest_path, "w", encoding="utf-8") as f:
    f.write(manifest_content.strip())

msix_output = os.path.join(output_dir, "ZenArchieve_v2.1.2_x64.msix")
print("[4/5] Packing MSIX package with makeappx.exe...")
cmd = [
    makeappx_exe,
    "pack",
    "/v",
    "/h", "SHA256",
    "/d", build_dir,
    "/p", msix_output,
    "/o"
]

res = subprocess.run(cmd, capture_output=True, text=True)
print(res.stdout)
if res.returncode != 0:
    print("STDERR:", res.stderr)
    raise RuntimeError(f"makeappx pack failed with exit code {res.returncode}")

print("[5/5] Deploying packages to installer_dist...")
dist_output = os.path.join(dist_dir, "ZenArchieve_v2.1.2_x64.msix")
shutil.copy2(msix_output, dist_output)

# Copy EXE installer to installer_dist
inno_exe = r"D:\Archieve\Archieve App\bin\installer\ZenArchieve_Setup_v2.1.2.exe"
if os.path.exists(inno_exe):
    dist_exe = os.path.join(dist_dir, "ZenArchieve_Setup_v2.1.2.exe")
    shutil.copy2(inno_exe, dist_exe)
    print(f"Copied Inno EXE to installer_dist: {dist_exe}")

msix_size_mb = os.path.getsize(dist_output) / (1024 * 1024)
print(f"=== MSIX & INSTALLER BUILD SUCCESSFUL! ===")
print(f"MSIX File: {dist_output} ({msix_size_mb:.2f} MB)")
