# Quick Reference: MSI Builder

## One-Time Setup

```powershell
# Install WiX Toolset (required once)
.\Install-WiX.ps1
# Takes 2-3 minutes, downloads ~200 MB from GitHub
```

## Build MSI

```powershell
# Build app → sign → create MSI
.\Build-InstallerMSI.ps1

# Output: HintOverlay.Installer\bin\Release\HintOverlay.msi
```

## Options

```powershell
# Build without signing
.\Build-InstallerMSI.ps1 -SkipSign

# Build without rebuilding app
.\Build-InstallerMSI.ps1 -SkipBuild

# Alternative builder (fallback if WiX fails)
.\New-HintOverlayMSI.ps1
```

## Install & Test

```powershell
# Install MSI
msiexec /i "HintOverlay.Installer\bin\Release\HintOverlay.msi"

# Install silently
msiexec /i "HintOverlay.Installer\bin\Release\HintOverlay.msi" /quiet

# Uninstall
msiexec /x "HintOverlay.Installer\bin\Release\HintOverlay.msi"

# Run installed application
& "C:\Program Files\HintOverlay\HintOverlay.exe"

# Check installation
Get-Item "C:\Program Files\HintOverlay\HintOverlay.exe"
```

## Production Setup

```powershell
# Sign the MSI itself
$signtool = "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe"

& $signtool sign `
  /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" `
  /p "test123" `
  /fd SHA256 `
  "HintOverlay.Installer\bin\Release\HintOverlay.msi"

# Verify MSI signature
Get-AuthenticodeSignature "HintOverlay.Installer\bin\Release\HintOverlay.msi"
```

## Customize MSI

Edit: `HintOverlay.Installer\Product.wxs`

**Change install folder:**
```xml
<Directory Id="INSTALLFOLDER" Name="HintOverlay" />
→
<Directory Id="INSTALLFOLDER" Name="MyAppName" />
```

**Add license:**
Replace: `HintOverlay.Installer\License.rtf`

**Add files:**
```xml
<File Id="MyFile" Source="path\to\file.ext" />
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "WiX not found" | Run `.\Install-WiX.ps1` |
| "Build failed" | Check error in console output |
| "Installation fails" | Run as Administrator |
| "Need custom installer" | Edit `Product.wxs` |

## File Locations

| Item | Path |
|------|------|
| Build Script | `Build-InstallerMSI.ps1` |
| WiX Project | `HintOverlay.Installer\HintOverlay.Installer.wixproj` |
| MSI Definition | `HintOverlay.Installer\Product.wxs` |
| License | `HintOverlay.Installer\License.rtf` |
| Output MSI | `HintOverlay.Installer\bin\Release\HintOverlay.msi` |
| Install Path | `C:\Program Files\HintOverlay\` |

## Typical Workflow

```
1. Make code changes in Visual Studio
2. Run: .\Build-InstallerMSI.ps1
3. Wait for build + sign + MSI creation
4. Install for testing: msiexec /i ...
5. Test application
6. Ready for distribution!
```

## Fastest Path

```powershell
# First time
.\Install-WiX.ps1

# Then, every time you want to build:
.\Build-InstallerMSI.ps1
```

## What's Installed

```
C:\Program Files\HintOverlay\
├── HintOverlay.exe          (Signed, uiAccess enabled)

Start Menu:
└── HintOverlay → HintOverlay

Desktop:
└── HintOverlay (Shortcut)

Registry:
└── HKCU:\Software\HintOverlay
```

## Next Step

```powershell
# Setup (first time)
.\Install-WiX.ps1

# Build MSI
.\Build-InstallerMSI.ps1

# Go! ✓
```

---

**Status**: ✅ MSI system ready!

For detailed info: See `MSI_SETUP_COMPLETE.md` or `MSI_INSTALLER_GUIDE.md`
