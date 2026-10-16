<#
.SYNOPSIS
    Pre-commit hook: lint the given PowerShell files with PSScriptAnalyzer.

.DESCRIPTION
    Reports Warning/Error findings using build/PSScriptAnalyzerSettings.psd1 and exits
    non-zero if there are any. Files are only read, never modified.

.EXAMPLE
    pwsh -NoProfile -File build/Invoke-PSScriptAnalyzer.ps1 build/build-complete.ps1
#>
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Path
)

$ErrorActionPreference = "Stop"

if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
    Write-Host "PSScriptAnalyzer is not installed. Run: Install-Module PSScriptAnalyzer -Scope CurrentUser"
    exit 1
}

$settings = Join-Path $PSScriptRoot "PSScriptAnalyzerSettings.psd1"
$failed = $false

foreach ($file in $Path) {
    $results = Invoke-ScriptAnalyzer -Path $file -Settings $settings -Severity Warning, Error
    if ($results) {
        $results | Format-Table -AutoSize RuleName, Severity, Line, Message | Out-String | Write-Host
        $failed = $true
    }
}

if ($failed) {
    exit 1
}
