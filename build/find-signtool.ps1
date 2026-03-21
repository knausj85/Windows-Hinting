$candidates = @()
$searchPaths = @(
    'C:\Program Files (x86)\Windows Kits\10\bin',
    'C:\Program Files\Windows Kits\10\bin'
)

foreach ($p in $searchPaths) {
    if (Test-Path $p) {
        $found = Get-ChildItem $p -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -like '*x64*' }
        foreach ($f in $found) {
            Write-Host "[Sign]   found: $($f.FullName)"
        }
        $candidates += $found
    } else {
        Write-Host "[Sign]   search path not present: $p"
    }
}

$fromPath = where.exe signtool 2>$null | Select-Object -First 1
if ($fromPath) {
    Write-Host "[Sign]   on PATH: $fromPath"
} else {
    Write-Host "[Sign]   signtool not on PATH"
}

if ($candidates.Count -eq 0 -and -not $fromPath) {
    Write-Host "[Sign]   WARNING: signtool.exe not found anywhere"
} else {
    $best = $candidates | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $best) { $best = $fromPath }
    Write-Host "[Sign]   will use: $best"
}
