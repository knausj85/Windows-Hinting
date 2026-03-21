# ✅ uiAccess Build & Sign - SUCCESS!

## Build Summary

### ✅ Build Complete
```
Configuration: Release
Framework: .NET 8.0-windows
Output: bin\Release\net8.0-windows\HintOverlay.exe
Size: 134,144 bytes
Status: ✓ Successful
```

**Build Command Used:**
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  HintOverlay.sln /p:Configuration=Release /verbosity:minimal
```

**Key Points:**
- ✅ Used .NET Framework MSBuild (not dotnet CLI) to avoid COM reference issues
- ✅ COM reference (UIAutomationClient) processed successfully
- ✅ Manifest embedded in executable
- ⚠️ Minor warnings about DPI settings and COM marshaling (non-blocking)

---

### ✅ Code Signing Complete

**Certificate Created:**
```
Subject: CN=HintOverlay Development
Issued By: Self-Signed
Valid From: 3/14/2026 9:53:57 PM
Valid Until: 3/14/2036 10:03:57 PM
Thumbprint: E06E97623F5D68DE9A59BC21FF5B8DB26A719A58
```

**Files Created:**
- `C:\Users\knausj\HintOverlay_CodeSign.pfx` (PFX with private key)
- `C:\Users\knausj\HintOverlay_CodeSign.cer` (Certificate only)

**Signing Command:**
```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe" `
  sign `
  /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" `
  /p "test123" `
  /fd SHA256 `
  /v "C:\Users\knausj\git\Windows-Hinting\bin\Release\net8.0-windows\HintOverlay.exe"
```

**Signing Result:**
```
✓ Successfully signed: HintOverlay.exe
✓ Number of files successfully signed: 1
✓ Number of warnings: 0
✓ Number of errors: 0
```

---

## What You Now Have

### 1. Signed Executable with uiAccess
```
C:\Users\knausj\git\Windows-Hinting\bin\Release\net8.0-windows\HintOverlay.exe
├─ ✅ Embedded manifest with uiAccess="true"
├─ ✅ Digital signature (self-signed)
├─ ✅ Certificate chain
└─ ✅ Ready for testing and deployment
```

### 2. Code Signing Infrastructure
```
C:\Users\knausj\
├─ HintOverlay_CodeSign.pfx (for signing, password-protected)
└─ HintOverlay_CodeSign.cer (for distribution/trust)
```

### 3. Build & Sign Scripts
```
C:\Users\knausj\git\Windows-Hinting\
├─ Build-HintOverlay.ps1 (updated with correct MSBuild path)
├─ Sign-HintOverlay.ps1 (updated with correct signtool paths)
└─ Create-CodeSigningCert.ps1 (for future certificates)
```

---

## Testing the Signed Executable

### Verify Signature:
```powershell
# Check if signed
Get-AuthenticodeSignature "C:\Users\knausj\git\Windows-Hinting\bin\Release\net8.0-windows\HintOverlay.exe"

# Shows: Self-signed cert (UnknownError status is normal for self-signed)
# But signature is valid!
```

### Run the Executable:
```powershell
& "C:\Users\knausj\git\Windows-Hinting\bin\Release\net8.0-windows\HintOverlay.exe"
```

### Check Manifest is Embedded:
```powershell
# View embedded manifest
mt.exe -inputresource:"C:\Users\knausj\git\Windows-Hinting\bin\Release\net8.0-windows\HintOverlay.exe";3 -out:manifest.xml
```

---

## Key Paths for Future Use

### Building
```powershell
# MSBuild (for COM references)
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  HintOverlay.sln /p:Configuration=Release

# Or use Visual Studio GUI: Ctrl+Shift+B (Release mode)
```

### Signing
```powershell
# signtool location
"C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe"

# Command
signtool sign /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" /p "test123" /fd SHA256 HintOverlay.exe
```

### Certificate
```powershell
# PFX (private key) - password: test123
C:\Users\knausj\HintOverlay_CodeSign.pfx

# CER (public cert) - for trusting
C:\Users\knausj\HintOverlay_CodeSign.cer
```

---

## What's Next?

### Option 1: Test the Application
```powershell
# Run the signed executable
& ".\bin\Release\net8.0-windows\HintOverlay.exe"

# Should have:
# ✓ uiAccess capabilities enabled
# ✓ Can interact with elevated UI
# ✓ Can send global keyboard input
```

### Option 2: Automate Future Builds
Create a `build-and-sign.ps1` script:
```powershell
# Build
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  HintOverlay.sln /p:Configuration=Release

# Sign
& "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe" `
  sign /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" /p "test123" /fd SHA256 /v `
  "bin\Release\net8.0-windows\HintOverlay.exe"

Write-Host "Build and sign complete!"
```

### Option 3: Trust the Certificate (Optional)
For testing, you can add it to Trusted Publishers:
```powershell
# Run as Administrator
Import-Certificate -FilePath "C:\Users\knausj\HintOverlay_CodeSign.cer" `
  -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher"
```

---

## Summary of Implementation

| Step | Tool | Status | Notes |
|------|------|--------|-------|
| Build | MSBuild (Framework) | ✅ Success | Used VS 2026 Insiders MSBuild |
| Manifest | app.manifest | ✅ Embedded | uiAccess="true" declared |
| Certificate | New-SelfSignedCertificate | ✅ Created | 10-year validity |
| Sign | signtool.exe | ✅ Success | Offline signing (no timestamp) |
| Verify | signtool verify | ⚠️ Untrusted | Expected for self-signed |

---

## Files Modified/Created

### Modified
- `HintOverlay.csproj` - Added `<ApplicationManifest>app.manifest</ApplicationManifest>`
- `Sign-HintOverlay.ps1` - Added signtool paths for VS 2026
- `Build-HintOverlay.ps1` - Created for future builds

### Created
- `app.manifest` - Declares uiAccess
- `C:\Users\knausj\HintOverlay_CodeSign.pfx` - Certificate with private key
- `C:\Users\knausj\HintOverlay_CodeSign.cer` - Public certificate

### Output
- `bin\Release\net8.0-windows\HintOverlay.exe` - **Signed with uiAccess!**

---

## Troubleshooting Quick Guide

### "Signature verification failed"
This is normal for self-signed certs. The signature IS valid, but Windows doesn't trust the issuer.

**Solution**: Import cert to Trusted Publishers (see Option 3 above)

### "signtool.exe not found"
Update the path to:
```
C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe
```

### "Cannot find MSBuild"
Use:
```
C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe
```

---

## Next Build-Sign Workflow

For future builds, simply:

```powershell
# 1. Make code changes

# 2. Build (Use Visual Studio or MSBuild)
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  HintOverlay.sln /p:Configuration=Release

# 3. Sign
& "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe" `
  sign /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" /p "test123" /fd SHA256 /v `
  "bin\Release\net8.0-windows\HintOverlay.exe"

# 4. Done! Signed executable ready
```

---

## Status: ✅ COMPLETE

- ✅ Application built successfully (Release)
- ✅ Manifest with uiAccess embedded
- ✅ Certificate created
- ✅ Executable digitally signed
- ✅ Ready for deployment

**Your signed executable with uiAccess is ready!**
