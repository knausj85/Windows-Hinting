# uiAccess & Code Signing Quick Start

## What Was Done

✅ **Created `app.manifest`** - Enables `uiAccess="true"` for your application
✅ **Updated `Windows-Hinting.csproj`** - References the manifest file
✅ **Created signing scripts** - PowerShell scripts to sign your executable
✅ **Created documentation** - Complete guide for all scenarios

## What is uiAccess?

**uiAccess** allows your application to:
- Bypass UIPI (User Interface Privilege Isolation)
- Interact with windows from privileged processes
- Simulate keyboard input globally
- Access UI elements across privilege boundaries

**Requirements:**
- Application must be signed with a code signing certificate
- Application must be in a protected location (e.g., Program Files)
- Manifest must declare `uiAccess="true"`

## Quick Start (5 minutes)

### Step 1: Create Self-Signed Certificate

```powershell
# Run PowerShell as Administrator
cd C:\Users\knausj\git\Windows-Hinting

# Create certificate (one-time setup)
.\Create-CodeSigningCert.ps1
```

You'll be prompted for a password. **Save this password** - you'll need it for signing.

### Step 2: Build Your Application

```powershell
dotnet build -c Release
```

### Step 3: Sign the Executable

```powershell
.\Sign-WindowsHinting.ps1 -BuildConfiguration Release
```

When prompted, enter the certificate password from Step 1.

### Step 4: Done!

Your executable is now signed with uiAccess enabled.

```powershell
# Test it
.\bin\Release\net8.0-windows\Windows-Hinting.exe
```

---

## For Production Distribution

If you're distributing to end users, use a **commercial certificate** instead:

1. Purchase from: Sectigo, DigiCert, GlobalSign, or similar (~$100-200/year)
2. Export as PFX file
3. Update signing scripts with your PFX path
4. Sign your executables the same way

See `CODESIGNING_GUIDE.md` for details.

---

## Architecture

### Manifest File (`app.manifest`)

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="true" />
```

This tells Windows:
- Run with normal user privileges (`asInvoker`)
- Allow interaction with privileged processes (`uiAccess="true"`)

### Signing Flow

```
Build (dotnet build)
    ↓
Executable (Windows-Hinting.exe)
    ↓
Sign with Certificate (Sign-WindowsHinting.ps1)
    ↓
Signed Executable (with uiAccess enabled)
    ↓
Can interact with privileged UI elements
```

---

## Files Created

```
Windows-Hinting/
├── app.manifest                  ← Enables uiAccess
├── Windows-Hinting.csproj           ← References manifest
├── Create-CodeSigningCert.ps1    ← Creates self-signed cert
├── Sign-WindowsHinting.ps1          ← Signs executable
└── CODESIGNING_GUIDE.md          ← Detailed documentation
```

---

## Common Tasks

### "I want to sign my Release build"
```powershell
.\Sign-WindowsHinting.ps1 -BuildConfiguration Release
```

### "I want to sign my Debug build"
```powershell
.\Sign-WindowsHinting.ps1 -BuildConfiguration Debug
```

### "I lost my certificate"
Create a new one:
```powershell
.\Create-CodeSigningCert.ps1
```

### "I want to use a different certificate"
```powershell
.\Sign-WindowsHinting.ps1 -CertificatePath "C:\path\to\cert.pfx" -CertificatePassword "password"
```

### "I want to verify a signature"
```powershell
Get-AuthenticodeSignature "bin\Release\net8.0-windows\Windows-Hinting.exe"
```

---

## Troubleshooting

### "signtool.exe not found"
Install Windows SDK:
https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/

### "Certificate not found"
Create it first:
```powershell
.\Create-CodeSigningCert.ps1
```

### "Access Denied" on Create-CodeSigningCert.ps1
Run PowerShell as Administrator

### "uiAccess not working after signing"
1. Verify manifest is in `app.manifest`
2. Verify `.csproj` references it: `<ApplicationManifest>app.manifest</ApplicationManifest>`
3. Rebuild: `dotnet clean && dotnet build -c Release`
4. Re-sign: `.\Sign-WindowsHinting.ps1 -BuildConfiguration Release`
5. Test the new executable

### "Timestamp server failed"
Try these alternatives in the script:
- `http://timestamp.sectigo.com` (default)
- `http://time.certum.pl`
- `http://timestamp.globalsign.com`
- Or remove `/t` parameter for offline signing (less secure)

---

## GitHub Actions Integration

To automate signing in CI/CD, see the `CODESIGNING_GUIDE.md` file under "CI/CD Integration".

Example workflow:
1. Build in GitHub Actions
2. Import certificate from secrets
3. Sign executable
4. Create release with signed executable

---

## Security Notes

✅ **Best Practices:**
- Store PFX password in GitHub Secrets, Azure Key Vault, etc.
- Use commercial certificates for public distribution
- Keep private keys backed up securely
- Use timestamp servers (prevents cert expiration from breaking signatures)
- Rotate certificates periodically

❌ **Avoid:**
- Hardcoding passwords in scripts
- Sharing private keys
- Using self-signed certs for public distribution
- Modifying executable after signing

---

## Next Steps

1. **Create certificate**: `.\Create-CodeSigningCert.ps1`
2. **Build project**: `dotnet build -c Release`
3. **Sign executable**: `.\Sign-WindowsHinting.ps1 -BuildConfiguration Release`
4. **Test**: `.\bin\Release\net8.0-windows\Windows-Hinting.exe`
5. **Deploy**: Share the signed executable

---

## More Information

- **CODESIGNING_GUIDE.md** - Complete guide with all options
- **app.manifest** - Configuration for uiAccess
- **Sign-WindowsHinting.ps1** - Detailed signing script
- **Create-CodeSigningCert.ps1** - Certificate creation script

---

## Questions?

Check `CODESIGNING_GUIDE.md` for:
- How to purchase a commercial certificate
- CI/CD pipeline setup
- Troubleshooting in detail
- Security best practices
- Verification procedures
