$ErrorActionPreference = 'Stop'
$Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop\mizan\dist\Mizan.exe'
if (-not (Test-Path $Exe)) { throw "Mizan.exe not found. Run Build.ps1 first." }
Start-Process -FilePath $Exe
