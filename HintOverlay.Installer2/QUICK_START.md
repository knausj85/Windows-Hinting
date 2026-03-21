# Quick Start Guide - Building and Deploying HintOverlay Installer

## Quick Build Commands

### Standard Build (without signing)
```powershell
# From solution root directory
msbuild HintOverlay.Installer2/HintOverlay.Installer2.wixproj -p:Configuration=Release

# Output: HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi
```

### Build with Code Signing (Production)
```powershell
# 1. Build and sign the main application
msbuild HintOverlay.csproj -p:Configuration=Release
signtool sign /f certificate.pfx /p password /fd SHA256 /tr http://timestamp.digicert.com bin\Release\net8.0-windows\HintOverlay.exe

# 2. Build the installer
msbuild HintOverlay.Installer2/HintOverlay.Installer2.wixproj -p:Configuration=Release
```

### Build Everything (Solution)
```powershell
# Build entire solution with all projects
msbuild HintOverlay.sln -p:Configuration=Release
```

## What the Installer Does

✅ **Installs HintOverlay to**: `C:\Program Files\HintOverlay\`
✅ **Creates shortcuts**: Desktop and Start Menu
✅ **Registers with Windows**: Uninstall support, auto-start (optional)
✅ **Configures UIAccess**: Registry entries for elevated UI interaction
✅ **License compliance**: Displays EULA during installation

## UIAccess - What You Need to Know

### The Problem
Windows restricts UI automation to prevent malware. HintOverlay needs "UIAccess" privilege to function properly with system elements.

### The Solution
**Your executable MUST be code-signed** for UIAccess to work.

### The Steps
1. Obtain or create a code-signing certificate
2. Sign your HintOverlay.exe binary
3. Install via this MSI installer
4. UIAccess automatically enabled ✓

### Self-Signed Certificate (Testing Only)
```powershell
# Create certificate
$cert = New-SelfSignedCertificate -CertStoreLocation cert:\LocalMachine\My -DnsName "Windows-Hinting" -Type CodeSigningCert

# Export to PFX
$pwd = ConvertTo-SecureString -String "password" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath cert.pfx -Password $pwd

# Sign executable
signtool sign /f cert.pfx /p password /fd SHA256 bin\Release\net8.0-windows\HintOverlay.exe

# Verify
signtool verify /pa bin\Release\net8.0-windows\HintOverlay.exe
```

## File Locations

```
HintOverlay.Installer2/
├── Package.wxs                    # Main installer definition
├── ExampleComponents.wxs           # Application files & shortcuts
├── UI.wxs                         # Installer UI configuration  
├── Folders.wxs                    # Directory structure
├── License.rtf                    # EULA displayed to users
├── INSTALLATION_GUIDE.md          # Detailed setup guide
├── UIACCESS_SETUP.md             # UIAccess technical details
├── HintOverlay.Installer2.wixproj # Project file
└── bin/Release/en-US/
    └── HintOverlay.msi            # Final installer (Ready to distribute)
```

## Installation on User Machine

### Standard Installation
```cmd
# Run the MSI installer
msiexec /i HintOverlay.msi

# Or double-click HintOverlay.msi in File Explorer
```

### Silent Installation
```cmd
# Install without UI prompts
msiexec /i HintOverlay.msi /qn

# Install with progress bar only
msiexec /i HintOverlay.msi /qb
```

### Uninstall
```cmd
# From Control Panel → Programs and Features
# OR command line:
msiexec /x HintOverlay.msi
```

## Customization Options

### Change Version Number
Edit `HintOverlay.Installer2.wixproj`:
```xml
<PropertyGroup>
  <ProductVersion>2.0.0.0</ProductVersion>
  ...
</PropertyGroup>
```

### Change Installation Path
Edit `Package.wxs`:
```xml
<Directory Id="INSTALLFOLDER" Name="MyAppName" />
```

### Disable Auto-Start
Edit `ExampleComponents.wxs` and remove the Run registry entry

### Custom License
Replace `License.rtf` with your own RTF file

## Verification Checklist

After building and installing:

- [ ] MSI file created: `HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi`
- [ ] HintOverlay.exe is signed (for production)
- [ ] Program installed to `C:\Program Files\HintOverlay\`
- [ ] Shortcuts created on Desktop and Start Menu
- [ ] Registry entries present at `HKLM:\Software\Windows-Hinting\HintOverlay`
- [ ] UIAccess manifest verified in executable
- [ ] Application launches without errors

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "MSI not found" | Check build output: `HintOverlay.Installer2\bin\Release\en-US\` |
| "Build fails" | Run `dotnet restore` in solution root |
| "UIAccess not working" | Sign HintOverlay.exe with code-signing certificate |
| "Cannot install to Program Files" | Run installer as Administrator |
| "File in use during install" | Close HintOverlay.exe and retry |

## Distribution

Your `HintOverlay.msi` is ready to:
- ✅ Share with users
- ✅ Deploy via SCCM/Intune
- ✅ Include on installation media
- ✅ Host on update servers

**Pro Tip**: Include with digital signature (signing the MSI itself is optional but recommended for enterprise)

---

For detailed technical information, see:
- `INSTALLATION_GUIDE.md` - Complete setup guide
- `UIACCESS_SETUP.md` - UIAccess technical reference
