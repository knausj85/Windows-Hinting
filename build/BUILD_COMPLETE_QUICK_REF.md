# Quick Reference - build-complete.ps1

## Most Common Commands

### ✅ Full Release Build with MSI (DEFAULT - Just Run It!)
```powershell
.\build\build-complete.ps1
```
**What it does:** Builds Release executable (signed) + creates MSI installer + verifies both

---

### ✅ Release Executable Only (No MSI)
```powershell
.\build\build-complete.ps1 -ExeOnly
```
**What it does:** Builds and signs Release executable only

---

### ✅ Debug Build (No MSI, No Signing)
```powershell
.\build\build-complete.ps1 -Configuration Debug
```
**What it does:** Builds Debug executable without installer or signing

---

### ✅ Debug with MSI
```powershell
.\build\build-complete.ps1 -Configuration Debug
# Already includes MSI by default!
```
**What it does:** Builds Debug executable + creates MSI

---

### ✅ Release without Signing (Testing)
```powershell
.\build\build-complete.ps1 -SkipSigning
```
**What it does:** Builds Release executable (unsigned) + creates MSI

---

### ✅ Custom Certificate
```powershell
.\build\build-complete.ps1 `
  -CertPath "C:\certs\mycert.pfx" `
  -CertPassword "mypassword"
```
**What it does:** Uses custom certificate for signing

---

## Using Batch Wrapper

```batch
REM Full build with MSI (DEFAULT)
build\build-complete.bat

REM Release only (no MSI)
build\build-complete.bat Release --exe-only

REM Debug build
build\build-complete.bat Debug

REM No signing
build\build-complete.bat Release --skip-signing
```

---

## Parameter Reference

| Parameter | Default | Options | Notes |
|-----------|---------|---------|-------|
| `-Configuration` | `Release` | `Debug`, `Release` | Build configuration |
| `-ExeOnly` | Not set | Switch | Skip MSI build |
| `-SkipSigning` | Not set | Switch | Skip code signing |
| `-CertPath` | `certs\HintOverlay_CodeSign.pfx` | File path | Path to signing certificate |
| `-CertPassword` | `HintOverlay_BuildCert_2024` | String | Certificate password |

---

## What Gets Built (By Default)

### Just run: `.\build\build-complete.ps1`
- ✅ `bin\Release\net8.0-windows\HintOverlay.exe` (signed)
- ✅ `HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi` (with signed executable)

### With `-ExeOnly`
- ✅ `bin\Release\net8.0-windows\HintOverlay.exe` (signed)

### With `-Configuration Debug`
- ✅ `bin\Debug\net8.0-windows\HintOverlay.exe` (unsigned)
- ✅ `HintOverlay.Installer2\bin\Debug\en-US\HintOverlay.msi` (with unsigned executable)

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "WiX installer project not found" | Ensure `HintOverlay.Installer2` exists; use `-ExeOnly` to skip MSI |
| "MSI file not found" | Check WiX installation; rebuild manually |
| "Certificate not found" | Verify cert path; run `generate-signing-cert.ps1` |
| "Don't want MSI this time" | Use `-ExeOnly` flag |

---

## Post-Build Steps

1. **Test MSI**
   ```powershell
   msiexec /i HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi
   ```

2. **Verify Installation**
   ```powershell
   & "C:\Program Files\HintOverlay\HintOverlay.exe"
   ```

3. **Check Signature**
   ```powershell
   Get-AuthenticodeSignature "C:\Program Files\HintOverlay\HintOverlay.exe"
   ```

---

## Summary

**Default (What most people want):**
```powershell
.\build\build-complete.ps1
```
Builds signed Release exe + MSI installer ✅

**Just the executable:**
```powershell
.\build\build-complete.ps1 -ExeOnly
```

**Debug build (also builds MSI):**
```powershell
.\build\build-complete.ps1 -Configuration Debug
```
