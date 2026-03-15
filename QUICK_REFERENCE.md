# Quick Reference: Build & Sign HintOverlay

## One-Command Build & Sign

```powershell
# Build
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" HintOverlay.sln /p:Configuration=Release; `
# Sign
& "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe" sign /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" /p "test123" /fd SHA256 /v "bin\Release\net8.0-windows\HintOverlay.exe"; `
# Done
Write-Host "OK Build and sign complete! Executable: bin\Release\net8.0-windows\HintOverlay.exe"
```

## Individual Commands

### Build (Release)
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" HintOverlay.sln /p:Configuration=Release
```

### Build (Debug)
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" HintOverlay.sln /p:Configuration=Debug
```

### Sign (Release)
```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe" sign /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" /p "test123" /fd SHA256 /v "bin\Release\net8.0-windows\HintOverlay.exe"
```

### Sign (Debug)
```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe" sign /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" /p "test123" /fd SHA256 /v "bin\Debug\net8.0-windows\HintOverlay.exe"
```

### Verify Signature
```powershell
Get-AuthenticodeSignature "bin\Release\net8.0-windows\HintOverlay.exe"
```

### Run Executable
```powershell
& "bin\Release\net8.0-windows\HintOverlay.exe"
```

## Credentials

| Item | Value |
|------|-------|
| Certificate Path | `C:\Users\knausj\HintOverlay_CodeSign.pfx` |
| Password | `test123` |
| Subject | CN=HintOverlay Development |
| Expires | 3/14/2036 |

## File Locations

| Item | Path |
|------|------|
| MSBuild | `C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe` |
| SignTool | `C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe` |
| Certificate (PFX) | `C:\Users\knausj\HintOverlay_CodeSign.pfx` |
| Certificate (CER) | `C:\Users\knausj\HintOverlay_CodeSign.cer` |
| Project Root | `C:\Users\knausj\git\Windows-Hinting` |
| Release Build | `bin\Release\net8.0-windows\HintOverlay.exe` |

## Shortcuts for PowerShell

Create these aliases in your PowerShell profile:

```powershell
# In $PROFILE or manually in each session:
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"
$signtool = "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe"
$cert = "C:\Users\knausj\HintOverlay_CodeSign.pfx"
$pwd = "test123"
$exe = "bin\Release\net8.0-windows\HintOverlay.exe"

# Then use:
# Build
& $msbuild HintOverlay.sln /p:Configuration=Release

# Sign
& $signtool sign /f $cert /p $pwd /fd SHA256 /v $exe

# Verify
Get-AuthenticodeSignature $exe
```

## What You Get

✅ **bin\Release\net8.0-windows\HintOverlay.exe**
- Contains embedded manifest with `uiAccess="true"`
- Digitally signed (self-signed cert)
- Can interact with elevated/privileged windows
- Ready for testing and deployment

## Common Tasks

### "I made code changes, rebuild"
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" HintOverlay.sln /p:Configuration=Release
```

### "I need to re-sign after rebuild"
```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe" sign /f "C:\Users\knausj\HintOverlay_CodeSign.pfx" /p "test123" /fd SHA256 /v "bin\Release\net8.0-windows\HintOverlay.exe"
```

### "I want to verify it's signed"
```powershell
Get-AuthenticodeSignature "bin\Release\net8.0-windows\HintOverlay.exe" | Format-List
```

### "I want to run it"
```powershell
& "bin\Release\net8.0-windows\HintOverlay.exe"
```

### "I want to build and sign in one go"
```powershell
# See "One-Command Build & Sign" at the top
```

## Status

✅ **Everything is set up and working!**

Latest build: 3/14/2026 10:03:10 PM
Latest sign: Success (offline signing)
Current version: Signed with uiAccess

Ready to go! 🚀
