# HintOverlay.Installer2 - UIAccess Installation Configuration

## ✅ Build Status

The HintOverlay.Installer2 project has been successfully configured and built with the latest WiX v6.0.2 toolset.

**Output MSI**: `HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi` (73,728 bytes)

## Installation Configuration

### Application Installation

**Installation Directory**: `C:\Program Files\HintOverlay\`

**Files Installed**:
- ✅ HintOverlay.exe (main application executable with embedded UIAccess manifest)
- ✅ License.rtf (displayed during installation)

**Shortcuts Created**:
- ✅ Desktop shortcut: "HintOverlay"
- ✅ Start Menu folder: "HintOverlay" 
- ✅ Start Menu shortcut: "HintOverlay"

### Registry Configuration

The installer creates and registers the following registry entries:

```
HKEY_LOCAL_MACHINE\Software\Windows-Hinting\HintOverlay\
  InstallPath = C:\Program Files\HintOverlay\
  Version = 1.0.0.0
  UIAccessEnabled = 1

HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run\
  HintOverlay = C:\Program Files\HintOverlay\HintOverlay.exe
  [Optional - enables auto-start]

HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\HintOverlay\
  DisplayName = HintOverlay
  DisplayVersion = 1.0.0.0
  Publisher = Windows-Hinting
  InstallLocation = C:\Program Files\HintOverlay\
```

## UIAccess Requirements & Signing

### ⚠️ CRITICAL: Code Signing is Required

For UIAccess functionality to work properly, **HintOverlay.exe must be code-signed** with a valid certificate.

#### Why Code Signing is Required

UIAccess is a Windows security feature that grants elevated privileges to interact with system UI. Microsoft requires digital signatures for UIAccess to:
1. Prevent malware from abusing UIAccess privileges
2. Ensure the application is from a trusted publisher
3. Allow proper integrity verification

#### Current Status

- ✅ Manifest is correctly embedded in HintOverlay.exe:
  ```xml
  <requestedExecutionLevel level="asInvoker" uiAccess="true" />
  ```
- ✅ Application is installed in proper location (Program Files)
- ⏳ **PENDING**: Binary code signing with a valid certificate

#### How to Sign the Executable

##### Option 1: Production Certificate (Recommended)

```powershell
# Using a certificate from a certificate authority
signtool sign /f certificate.pfx /p password /fd SHA256 /tr http://timestamp.digicert.com bin\Release\net8.0-windows\HintOverlay.exe

# Verify the signature
signtool verify /pa bin\Release\net8.0-windows\HintOverlay.exe
```

##### Option 2: Self-Signed Certificate (Development/Testing)

```powershell
# Create a self-signed code-signing certificate
$cert = New-SelfSignedCertificate `
  -CertStoreLocation cert:\LocalMachine\My `
  -DnsName "Windows-Hinting" `
  -Type CodeSigningCert `
  -KeyUsage DigitalSignature `
  -KeySpec Signature `
  -KeyLength 2048

# Export to PFX format
$pwd = ConvertTo-SecureString -String "your-password" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath cert.pfx -Password $pwd -ChainOption BuildChain

# Sign the executable
signtool sign /f cert.pfx /p your-password /fd SHA256 bin\Release\net8.0-windows\HintOverlay.exe

# Trust the certificate (for testing)
# Import the certificate to Trusted Root:
Import-Certificate -FilePath cert.cer -CertStoreLocation Cert:\LocalMachine\Root
```

#### Build Process with Signing

1. **Build HintOverlay**:
   ```powershell
   msbuild HintOverlay.csproj -p:Configuration=Release
   ```

2. **Sign the executable**:
   ```powershell
   signtool sign /f certificate.pfx /p password /fd SHA256 /tr http://timestamp.server bin\Release\net8.0-windows\HintOverlay.exe
   ```

3. **Build the installer**:
   ```powershell
   msbuild HintOverlay.Installer2/HintOverlay.Installer2.wixproj -p:Configuration=Release
   ```

### Manifest Configuration

The manifest is already correctly configured in `app.manifest`:

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="true" />
```

This allows the application to:
- Run with the same privilege level as the invoking process ("asInvoker")
- Bypass UIPI to interact with elevated UI elements ("uiAccess='true'")

### Platform Configuration

- **Target**: 64-bit (x64)
- **Program Files**: `C:\Program Files\HintOverlay\` (native x64 location)
- **Architecture**: Always64 components throughout

## WiX Configuration Files

### Package.wxs
- Main package definition
- Feature definitions
- Directory structure (64-bit Program Files)
- License file reference
- Upgrade strategy

### ExampleComponents.wxs
- Main executable installation
- Shortcut definitions (Desktop and Start Menu)
- Registry entries for UIAccess tracking
- Installation path configuration

### UI.wxs
- Installer UI properties
- Prevents modification/repair of installed application

### Folders.wxs
- Empty (directory structure defined in Package.wxs)

### License.rtf
- License agreement displayed to users during installation
- Editable for custom terms

## Testing UIAccess Installation

After installation, verify UIAccess is working:

```powershell
# Check if executable is signed
signtool verify /pa "C:\Program Files\HintOverlay\HintOverlay.exe"

# Check registry entries
Get-ItemProperty -Path "HKLM:\Software\Windows-Hinting\HintOverlay"

# Verify manifest
# Use Windows Resource Hacker or similar tool to inspect embedded manifest
```

## Troubleshooting

### Issue: "UIAccess is being ignored"
**Cause**: Binary is not digitally signed
**Solution**: Sign HintOverlay.exe with a valid code-signing certificate

### Issue: "This program requires privilege elevation"
**Cause**: UIAccess manifest not embedded correctly
**Solution**: Verify app.manifest is in the project and rebuild

### Issue: "Access Denied" when accessing system UI
**Cause**: Not installed in Program Files or UIAccess not enabled
**Solution**: Reinstall using this MSI installer

### Issue: "Cannot run executable at this location"
**Cause**: File may be blocked by SmartScreen
**Solution**: Create an exception or sign with a trusted certificate

## Security Considerations

⚠️ **Important**: UIAccess grants significant privileges:

1. **Always use trusted certificates** - Only use certificates from recognized CAs
2. **Validate all inputs** - Never trust automation requests blindly
3. **Keep updated** - Regularly update Windows and the application
4. **Monitor access** - Check registry for unauthorized modifications
5. **Use strong permissions** - Restrict installer to authorized users

## Advanced Configuration

### Disable Auto-Start
Edit `ExampleComponents.wxs` and remove/comment out:
```xml
<RegistryValue
  Root="HKLM"
  Key="Software\Microsoft\Windows\CurrentVersion\Run"
  Name="HintOverlay"
  Type="string"
  Value="[INSTALLFOLDER]HintOverlay.exe" />
```

### Add Additional Files
Add `<File>` elements to `ExampleComponents.wxs` component group and rebuild

### Customize License
Edit `License.rtf` with any RTF editor

## References

- [Microsoft UIAccess Security Documentation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)
- [Code Signing with Signtool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
- [WiX Toolset v6 Documentation](https://wixtoolset.org/)
- [Windows Application Manifest Files](https://learn.microsoft.com/en-us/windows/win32/sbscs/manifests)

---

**Next Steps**:
1. ✅ Sign HintOverlay.exe with a code-signing certificate
2. ✅ Rebuild the installer project
3. ✅ Test the MSI on a clean Windows system
4. ✅ Verify UIAccess is functioning properly
