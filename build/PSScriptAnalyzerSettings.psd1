@{
    ExcludeRules = @(
        # Write-Host is intentional in build/CI scripts for console output
        'PSAvoidUsingWriteHost',
        # ConvertTo-SecureString with plaintext is required for cert export in build scripts
        'PSAvoidUsingConvertToSecureStringWithPlainText'
    )
}
