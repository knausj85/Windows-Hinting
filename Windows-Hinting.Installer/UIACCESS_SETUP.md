# Windows-Hinting.Installer - UIAccess Configuration Guide

This WiX installer is configured to properly install Windows-Hinting with UIAccess support.

## Overview

UIAccess is a Windows security feature that allows applications to bypass UIPI (User Interface Privilege Isolation) and interact with UI elements from elevated processes. This is essential for UI automation tools like Windows-Hinting.

## Installation Details

### File Installation
- **Executable**: `Windows-Hinting.exe` is installed to `C:\Program Files\Windows-Hinting\`
- **Architecture**: 64-bit (x64) support
- **Checksums**: File integrity validation is enabled

### Features
1. **Windows-Hinting Executable** - Main application with UIAccess manifest
2. **Desktop Shortcut** - Quick access from desktop
3. **Start Menu Shortcut** - Application folder in Start Menu
4. **Registry Entries** - Uninstall info and UIAccess configuration

### Registry Configuration
The installer creates the following registry entries:

```
HKEY_LOCAL_MACHINE\Software\Windows-Hinting\Windows-Hinting
  - InstallPath: C:\Program Files\Windows-Hinting\
  - Version: [ProductVersion]
  - UIAccessEnabled: 1
```

## UIAccess Requirements

### 1. Binary Code Signing (REQUIRED for UIAccess to work)

The Windows-Hinting.exe **MUST** be digitally signed with a valid certificate. Without a valid signature, UIAccess will be ignored.

#### Sign with a Production Certificate
```powershell
signtool sign /f certificate.pfx /p password /fd SHA256 /tr http://timestamp.digicert.com/scripts/timestamp.dll Windows-Hinting.exe
```

#### For Development/Testing - Create Self-Signed Certificate
```powershell
# Create certificate (Windows 10+)
$cert = New-SelfSignedCertificate -CertStoreLocation cert:\LocalMachine\My -DnsName "Windows-Hinting" -Type CodeSigningCert

# Export to PFX
$pwd = ConvertTo-SecureString -String "password" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath cert.pfx -Password $pwd

# Sign the executable
signtool sign /f cert.pfx /p password /fd SHA256 Windows-Hinting.exe

# Verify signature
signtool verify /pa Windows-Hinting.exe
```

### 2. Manifest Configuration (✅ Already Set)

The `app.manifest` in Windows-Hinting.csproj is correctly configured:

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="true" />
```

This manifest is embedded in the executable during build.

### 3. Installation Location (✅ Configured)

UIAccess only works when the application is installed in a trusted location:
- ✅ `C:\Program Files\` (configured in this installer)
- ✅ `C:\Program Files (x86)\` for 32-bit
- ❌ User AppData folders won't work with UIAccess
- ❌ Portable/USB installations won't work

### 4. Code Integrity (Windows 11 Secure Boot)

On Windows 11 with Secure Boot enabled, additional requirements may apply:
- Kernel-mode driver signing may be required
- Check Event Viewer for code integrity violations

## Building the Installer

### From Visual Studio
```
1. Open Windows-Hinting.sln
2. Ensure Windows-Hinting.exe is code-signed
3. Build Windows-Hinting.Installer project
4. MSI will be generated in output directory
```

### From Command Line
```powershell
# Build main application
dotnet build Windows-Hinting.csproj -c Release

# Sign the executable (required for UIAccess)
signtool sign /f cert.pfx /p password /fd SHA256 bin\Release\net8.0-windows\Windows-Hinting.exe

# Build the installer
dotnet build Windows-Hinting.Installer\Windows-Hinting.Installer.wixproj -c Release
```

## Post-Installation

### Verify UIAccess is Working
```powershell
# Check embedded manifest
signtool verify /pa "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"

# Verify registry entries
Get-ItemProperty -Path "HKLM:\Software\Windows-Hinting\Windows-Hinting"
```

### Troubleshooting UIAccess Issues

#### "UIAccess is ignored"
- **Cause**: Binary is not signed
- **Fix**: Sign Windows-Hinting.exe with a valid code-signing certificate

#### "Access is denied" when accessing privileged UI
- **Cause**: UIAccess may be disabled or not working properly
- **Check**: 
  - Verify executable is signed: `signtool verify /pa Windows-Hinting.exe`
  - Check Event Viewer (System) for code integrity warnings
  - Verify manifest is embedded: `ExifTool.exe -ALL Windows-Hinting.exe | grep -i manifest`

#### "File not found" or "Cannot access"
- **Cause**: Application not installed in Program Files
- **Fix**: Reinstall using this MSI installer

## Security Considerations

UIAccess grants elevated UI interaction privileges. Important security notes:

1. **Always sign with trusted certificates** - Use certificates from recognized CAs
2. **Validate user input** - Even with UIAccess, validate all automation requests
3. **Keep updated** - Regularly update Windows and the application
4. **Audit access** - Monitor registry entries for unauthorized changes

## Advanced Configuration

### Modify Auto-Start Behavior
Edit `Package.wxs` to remove/modify the Run registry entry:
```xml
<!-- Remove or comment out to disable auto-start -->
<RegistryValue
  Root="HKLM"
  Key="Software\Microsoft\Windows\CurrentVersion\Run"
  Name="Windows-Hinting"
  Type="string"
  Value="[INSTALLFOLDER]Windows-Hinting.exe" />
```

### Add Additional Components
To add files, libraries, or resources:
1. Add `<File>` elements to the `MainComponents` ComponentGroup
2. Reference them in the Product Feature
3. Rebuild the installer

## References

- [Microsoft UIAccess Security Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)
- [Code Signing with signtool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
- [WiX Toolset Documentation](https://wixtoolset.org/)
- [Windows Manifest Files](https://learn.microsoft.com/en-us/windows/win32/sbscs/manifests)

## Support

For issues with the installer or UIAccess configuration, check:
1. Event Viewer → Windows Logs → System
2. Event Viewer → Windows Logs → Application
3. Verbose MSI logging: `msiexec /i Windows-Hinting.msi /l*v logfile.txt`

---

**Important**: UIAccess requires code signing. This is a Windows security requirement, not a limitation of the installer.
