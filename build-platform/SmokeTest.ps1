$ErrorActionPreference = 'Stop'
$Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop\mizan\dist\Mizan.exe'
if (-not (Test-Path $Exe)) { throw 'Mizan.exe not found.' }
$fi = Get-Item $Exe
if ($fi.Length -lt 100000) { throw "Mizan.exe is unexpectedly small: $($fi.Length) bytes" }
$bytes = [System.IO.File]::ReadAllBytes($Exe)
if ($bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw 'Invalid PE signature.' }
Write-Host "PE validation passed: $($fi.Length) bytes" -ForegroundColor Green
