<#
.SYNOPSIS
    Pre-commit hook: fix and lint the given PowerShell files with PSScriptAnalyzer.

.DESCRIPTION
    First applies PSScriptAnalyzer's automatic fixes (rules that support -Fix, such as
    expanding aliases or correcting cmdlet casing), then reports any remaining
    Warning/Error findings using build/PSScriptAnalyzerSettings.psd1. Exits non-zero if
    findings remain. pre-commit (and pre-commit.ci lite on PRs) picks up the fixed files.

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
    Invoke-ScriptAnalyzer -Path $file -Settings $settings -Severity Warning, Error -Fix | Out-Null

    $results = Invoke-ScriptAnalyzer -Path $file -Settings $settings -Severity Warning, Error
    if ($results) {
        $results | Format-Table -AutoSize RuleName, Severity, Line, Message | Out-String | Write-Host
        $failed = $true
    }
}

if ($failed) {
    exit 1
}
