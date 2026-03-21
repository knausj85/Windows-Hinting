# GitHub Actions Code Signing Setup

This guide explains how to configure Authenticode code signing for automated
GitHub Actions builds using a certificate stored as a repository secret.

## How It Works

On every push, the `build-release` job:

1. **Decodes** the `SIGNING_CERT_BASE64` secret into a temporary PFX file stored
   in `$RUNNER_TEMP` (outside the workspace)
2. **Builds** `Windows-Hinting.exe` and the MSI installer, signing the exe via `signtool.exe`
3. **Cleans up** the temporary PFX file immediately after the build, even if the
   build fails (`if: always()`)

If the secrets are not configured the build falls back to an unsigned release
automatically — no workflow changes needed.

## Setup Steps

### 1. Obtain a PFX Certificate

**Option A — Self-signed (testing only):**
```powershell
.\build\generate-signing-cert.ps1 -Force
# Creates certs\WindowsHinting_CodeSign.pfx  (password: WindowsHinting_BuildCert_2024)
```
> **Note:** A self-signed certificate satisfies the binary structure required to
> set `uiAccess="true"` but Windows SmartScreen will still warn users and the
> certificate will not be trusted for UIAccess at runtime. Use a CA-issued
> certificate for production.

**Option B — CA-issued certificate (production):**
Purchase a code signing certificate from a trusted CA (e.g. DigiCert, Sectigo)
and export it as a PFX file with a password.

---

### 2. Base64-Encode the PFX

Run this in PowerShell from the repo root, replacing the path if needed:

```powershell
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes('certs\WindowsHinting_CodeSign.pfx'))
$base64 | Set-Clipboard
Write-Host "Copied $($base64.Length) characters to clipboard"
```

---

### 3. Add Secrets to the Repository

Go to your repository on GitHub:
**Settings → Secrets and variables → Actions → New repository secret**

| Secret name            | Value                                                  |
|------------------------|--------------------------------------------------------|
| `SIGNING_CERT_BASE64`  | The full base64 string from Step 2                     |
| `SIGNING_CERT_PASSWORD`| The PFX password (e.g. `WindowsHinting_BuildCert_2024`)   |

---

### 4. Verify

Push a commit to any branch. In the **Actions** tab, open the **Release Build + MSI**
job and check the **Build Release + MSI** step. You should see:

```
Building with code signing (cert: D:\a\_temp\codesign.pfx)
```

If the secrets are absent you will see instead:

```
SIGNING_CERT_BASE64 secret not configured — build will be unsigned
```

---

## Certificate Rotation

To rotate the certificate:
1. Generate or obtain the new PFX
2. Re-run Step 2 to get the new base64 string
3. Update the `SIGNING_CERT_BASE64` and (if changed) `SIGNING_CERT_PASSWORD` secrets
4. The next build will automatically use the new certificate
