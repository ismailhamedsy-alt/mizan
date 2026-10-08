$ErrorActionPreference = 'Stop'
$AppDir = Join-Path $env:LOCALAPPDATA 'MizanDesktop'
$Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop\mizan\dist\Mizan.exe'

if (-not (Test-Path $Exe)) { throw "Mizan.exe not found: $Exe" }

# Start from a clean first-run database.
if (Test-Path $AppDir) { Remove-Item $AppDir -Recurse -Force }

$self = Start-Process -FilePath $Exe -ArgumentList '--self-test-login' -PassThru -Wait
if ($self.ExitCode -ne 0) {
  throw "Login self-test failed with exit code $($self.ExitCode)."
}

if (Test-Path (Join-Path $AppDir 'startup-crash.log')) {
  $details = Get-Content (Join-Path $AppDir 'startup-crash.log') -Raw
  throw ('Login self-test created a startup crash log:' + [Environment]::NewLine + $details)
}

# Reuse the initialized database and confirm normal UI startup.
$p = Start-Process -FilePath $Exe -PassThru
try {
  Start-Sleep -Seconds 7
  $crashLog = Join-Path $AppDir 'startup-crash.log'
  if (Test-Path $crashLog) {
    $details = Get-Content $crashLog -Raw
    throw ('Normal startup created a crash log:' + [Environment]::NewLine + $details)
  }
  if ($p.HasExited) {
    throw "Mizan.exe exited during normal startup with code $($p.ExitCode)."
  }
  Write-Host "First-run login + normal startup smoke tests passed. PID=$($p.Id)" -ForegroundColor Green
}
finally {
  if (-not $p.HasExited) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
  }
}
