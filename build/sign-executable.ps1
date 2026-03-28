param(
    [Parameter(Mandatory)]
    [string]$CertPath,
    [Parameter(Mandatory)]
    [string]$CertPassword,
    [Parameter(Mandatory)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

if (-not $CertPassword) {
    Write-Error "[Sign] No certificate password provided. Set the CERT_PASSWORD environment variable before running the build."
    exit 1
}

# Locate signtool.exe
$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*x64*' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $signtool) {
    $signtool = Get-ChildItem 'C:\Program Files\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*x64*' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $signtool) {
    $signtool = (where.exe signtool 2>$null | Select-Object -First 1)
}

Write-Host "[Sign] signtool path: $signtool"
if (-not $signtool) {
    Write-Error "[Sign] signtool.exe not found - install Windows SDK"
    exit 1
}

# Build signtool arguments
$signArgs = @('sign', '/f', $CertPath, '/p', $CertPassword)
$signArgs += '/t'
$signArgs += 'http://timestamp.digicert.com'
$signArgs += '/fd'
$signArgs += 'SHA256'
$signArgs += $ExePath

Write-Host "[Sign] Signing: $ExePath"
& $signtool @signArgs
exit $LASTEXITCODE
