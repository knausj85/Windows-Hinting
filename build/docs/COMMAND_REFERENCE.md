# Command Reference - build-complete.ps1

## 🎯 Most Common Use Case

```powershell
cd C:\Users\knausj\git\Windows-Hinting
.\build\build-complete.ps1
```

**What it does:**
- ✅ Builds Release executable
- ✅ Signs it with code certificate
- ✅ Verifies signature
- ✅ Builds MSI installer
- ✅ Verifies signed exe in MSI
- ✅ Outputs ready-to-distribute files

**Output:**
```
Executable: bin\Release\net8.0-windows\Windows-Hinting.exe
Installer:  Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
```

---

## All Command Examples

### 1️⃣ Release with MSI (DEFAULT - Just Run It!)
```powershell
.\build\build-complete.ps1
```
Same as: `.\build\build-complete.ps1 -Configuration Release`

### 2️⃣ Release Executable Only
```powershell
.\build\build-complete.ps1 -ExeOnly
```
Same as: `.\build\build-complete.ps1 -Configuration Release -ExeOnly`

### 3️⃣ Debug Build Only
```powershell
.\build\build-complete.ps1 -Configuration Debug -ExeOnly
```

### 4️⃣ Debug with MSI
```powershell
.\build\build-complete.ps1 -Configuration Debug
```

### 5️⃣ Release MSI without Signing
```powershell
.\build\build-complete.ps1 -SkipSigning
```

### 6️⃣ Custom Certificate Path
```powershell
.\build\build-complete.ps1 `
  -CertPath "C:\path\to\certificate.pfx" `
  -CertPassword "password123"
```

### 7️⃣ Release without Signing (Testing)
```powershell
.\build\build-complete.ps1 -SkipSigning
```

### 8️⃣ Debug MSI without Signing
```powershell
.\build\build-complete.ps1 -Configuration Debug -SkipSigning
```

---

## Batch File Commands

If using `build\build-complete.bat`:

```batch
REM Release with MSI (DEFAULT)
build\build-complete.bat

REM Release only
build\build-complete.bat Release --exe-only

REM Debug with MSI
build\build-complete.bat Debug

REM Release without signing
build\build-complete.bat Release --skip-signing
```

---

## Parameter Details

### `-Configuration` (Default: "Release")
```powershell
-Configuration Release   # Build Release
-Configuration Debug     # Build Debug
```

### `-ExeOnly` (Flag, not set by default)
```powershell
-ExeOnly                # Skip MSI, just build executable
# Without: Builds both exe and MSI
```

### `-SkipSigning` (Flag, not set by default)
```powershell
-SkipSigning           # Skip code signing
# Without: Sign executable (if Release)
```

### `-CertPath` (Default: "certs\WindowsHinting_CodeSign.pfx")
```powershell
-CertPath "C:\my\cert.pfx"  # Custom certificate location
```
---

## Step Count Reference

| Configuration | -ExeOnly | Steps | What Happens |
|---|---|---:|---|
| Release | No | 4 | Build exe → Sign → Verify → Build MSI → Verify MSI |
| Release | Yes | 1 | Build exe → Sign → Verify |
| Debug | No | 3 | Build exe → Build MSI → Verify MSI |
| Debug | Yes | 1 | Build exe |

---

## Output Examples

### Step Output (Release, default)
```
[1/4] Building Windows-Hinting executable...
[OK] Executable build completed successfully

[2/4] Verifying executable signature...
[OK] Executable is signed
  Subject: CN=Windows-Hinting
  Valid until: 12/31/2025

[3/4] Building MSI installer...
[OK] Installer build completed successfully

[4/4] Verifying signed executable in MSI...
[OK] MSI contains signed executable
  Subject: CN=Windows-Hinting
  Valid until: 12/31/2025
```

### Final Output
```
Build Summary:
  Configuration: Release
  Executable: bin\Release\net8.0-windows\Windows-Hinting.exe
  Signing: Enabled
  Installer: Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
  Installer Size: 5.2 MB
```

---

## Error Scenarios

### Certificate not found
```
ERROR: Certificate not found at: certs\WindowsHinting_CodeSign.pfx
```
**Solution:**
```powershell
.\build\generate-signing-cert.ps1   # Generate new cert
# OR
-SkipSigning                        # Skip signing
```

### WiX installer not found
```
ERROR: WiX installer project not found at: ...Windows-Hinting.Installer.wixproj
```
**Solution:**
- Ensure `Windows-Hinting.Installer` directory exists
- Check `.wixproj` file exists
- Install WiX Toolset
- Use `-ExeOnly` to skip MSI

### MSI not created
```
ERROR: MSI file not found at expected location
```
**Solution:**
- Check WiX build output for errors
- Rebuild manually: `msbuild Windows-Hinting.Installer\Windows-Hinting.Installer.wixproj /p:Configuration=Release`

---

## Verification Commands

### Check executable is signed
```powershell
Get-AuthenticodeSignature bin\Release\net8.0-windows\Windows-Hinting.exe
```

### Check installed executable is signed
```powershell
Get-AuthenticodeSignature "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"
```

### Test MSI installation
```powershell
msiexec /i Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
```

### Extract exe from MSI (requires lessmsi)
```powershell
lessmsi x Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi output\
```

---

## Scripting Examples

### CI/CD Pipeline
```powershell
# In your CI/CD script
$buildResult = & .\build\build-complete.ps1 `
  -Configuration Release `
  -CertPath $env:CERT_PATH `
  -CertPassword $env:CERT_PASSWORD

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!"
    exit 1
}

# Archive outputs
$msiPath = "Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi"
Copy-Item $msiPath "artifacts\"
Copy-Item "bin\Release\net8.0-windows\Windows-Hinting.exe" "artifacts\"
```

### Scheduled Build
```powershell
# Schedule in Windows Task Scheduler
$taskAction = New-ScheduledTaskAction -Execute "powershell.exe" `
  -Argument "-File C:\path\build\build-complete.ps1"

Register-ScheduledTask -TaskName "Windows-Hinting-Build" `
  -Action $taskAction `
  -Trigger @(New-ScheduledTaskTrigger -Daily -At 2am)
```

---

## Next Steps After Build

1. **Test the installer**
   ```powershell
   msiexec /i Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi /passive
   ```

2. **Verify signed executable**
   ```powershell
   Get-AuthenticodeSignature "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"
   ```

3. **Test application**
   ```powershell
   & "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"
   ```

4. **Sign MSI** (optional, recommended for distribution)
   ```powershell
   signtool sign /f "certs\WindowsHinting_CodeSign.pfx" `
     /p "*****" `
     /fd SHA256 `
     Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
   ```

---

## Summary

**Default (What most people want):**
```powershell
.\build\build-complete.ps1
```

That's it! Everything else follows automatically:
- ✅ Compilation
- ✅ Code signing
- ✅ MSI creation
- ✅ Signature verification
- ✅ Ready to distribute

**Just the executable:**
```powershell
.\build\build-complete.ps1 -ExeOnly
```

**Debug build (also creates MSI):**
```powershell
.\build\build-complete.ps1 -Configuration Debug
```
