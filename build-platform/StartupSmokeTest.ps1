$ErrorActionPreference = 'Stop'
$AppDir = Join-Path $env:LOCALAPPDATA 'MizanDesktop'
$Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop\mizan\dist\Mizan.exe'

if (-not (Test-Path $Exe)) { throw "Mizan.exe not found: $Exe" }

# CI must validate a true first-run installation, not a reused database.
if (Test-Path $AppDir) { Remove-Item $AppDir -Recurse -Force }

$p = Start-Process -FilePath $Exe -PassThru
try {
  Start-Sleep -Seconds 7
  $crashLog = Join-Path $AppDir 'startup-crash.log'
  if (Test-Path $crashLog) {
    $details = Get-Content $crashLog -Raw
    throw ('Startup crash log was created on first run:' + [Environment]::NewLine + $details)
  }
  if ($p.HasExited) { throw "Mizan.exe exited during first-run startup with code $($p.ExitCode)." }
  Write-Host "First-run startup smoke test passed. Login window remained open. PID=$($p.Id)" -ForegroundColor Green
} finally {
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}