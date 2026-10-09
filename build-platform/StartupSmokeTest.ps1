$ErrorActionPreference = 'Stop'
$AppDir = Join-Path $env:LOCALAPPDATA 'MizanDesktop'
$Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop\mizan\dist\Mizan.exe'

if (-not (Test-Path $Exe)) { throw "Mizan.exe not found: $Exe" }

# Start from a clean first-run database.
if (Test-Path $AppDir) { Remove-Item $AppDir -Recurse -Force }

$tests = @('--self-test-login', '--self-test-legacy-login', '--self-test-login-recovery', '--self-test-views')
foreach ($testArg in $tests) {
  $self = Start-Process -FilePath $Exe -ArgumentList $testArg -PassThru -Wait
  if ($self.ExitCode -ne 0) {
    throw "Authentication test $testArg failed with exit code $($self.ExitCode)."
  }
  $crashLog = Join-Path $AppDir 'startup-crash.log'
  if (Test-Path $crashLog) {
    $details = Get-Content $crashLog -Raw
    throw ('Authentication test created a startup crash log:' + [Environment]::NewLine + $details)
  }
}
Write-Host 'Fresh login, legacy-account migration, and admin recovery tests passed.' -ForegroundColor Green

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
