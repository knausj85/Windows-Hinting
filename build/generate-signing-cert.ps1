param([string]$CertName = "WindowsHinting", [string]$CertPath = "$PSScriptRoot\..\Windows-Hinting\certs\WindowsHinting_CodeSign.pfx", [SecureString]$CertPassword, [int]$ValidYears = 10, [switch]$Force)

$ErrorActionPreference = "Stop"
$CertDir = Split-Path -Parent $CertPath

if ((Test-Path $CertDir) -eq $false) {
    Write-Host "Creating certificates directory: $CertDir"
    New-Item -ItemType Directory -Path $CertDir -Force | Out-Null
}

if ((Test-Path $CertPath) -eq $true) {
    if ($Force -eq $false) {
        Write-Host "Certificate already exists at: $CertPath"
        exit 0
    }
}

Write-Host "Generating self-signed code signing certificate..."
Write-Host "  Name: $CertName"
Write-Host "  Path: $CertPath"
Write-Host "  Valid for: $ValidYears years"

$cert = New-SelfSignedCertificate -CertStoreLocation cert:\CurrentUser\My -Subject "CN=$CertName" -KeyUsage DigitalSignature -Type CodeSigningCert -NotAfter (Get-Date).AddYears($ValidYears)

Write-Host "Created certificate with thumbprint: $($cert.Thumbprint)"

$SecurePassword = ConvertTo-SecureString -String $CertPassword -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $CertPath -Password $SecurePassword -Force | Out-Null

Write-Host "Exported certificate to: $CertPath"
Write-Host ""
Write-Host "Certificate Details:"
Write-Host "  Subject: $($cert.Subject)"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  Not Before: $($cert.NotBefore)"
Write-Host "  Not After: $($cert.NotAfter)"
Write-Host ""
Write-Host "Certificate generation complete!"
