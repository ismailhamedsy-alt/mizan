$ErrorActionPreference = 'Stop'
$AppDir = Join-Path $env:LOCALAPPDATA 'MizanDesktop'
$Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop\mizan\dist\Mizan.exe'

if (-not (Test-Path $Exe)) { throw "Mizan.exe not found: $Exe" }

# Start from a clean first-run database.
if (Test-Path $AppDir) { Remove-Item $AppDir -Recurse -Force }

function Invoke-MizanSelfTest([string]$Argument) {
  Write-Host "Running $Argument ..." -ForegroundColor Cyan
  $process = Start-Process -FilePath $Exe -ArgumentList $Argument -PassThru
  if (-not $process.WaitForExit(90000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    $uiLog = Join-Path $AppDir 'ui-smoke.log'
    if (Test-Path $uiLog) { Get-Content $uiLog }
    $crashLog = Join-Path $AppDir 'startup-crash.log'
    if (Test-Path $crashLog) { Get-Content $crashLog -Raw }
    throw "Timed out after 90 seconds running $Argument."
  }

  if ($process.ExitCode -ne 0) {
    $uiLog = Join-Path $AppDir 'ui-smoke.log'
    if (Test-Path $uiLog) {
      Write-Host 'UI smoke progress:'
      Get-Content $uiLog
    }
    $crashLog = Join-Path $AppDir 'startup-crash.log'
    if (Test-Path $crashLog) {
      Write-Host 'Application crash log:'
      Get-Content $crashLog -Raw
    }
    throw "Self-test $Argument failed with exit code $($process.ExitCode)."
  }

  $crashLog = Join-Path $AppDir 'startup-crash.log'
  if (Test-Path $crashLog) {
    Get-Content $crashLog -Raw
    throw "Self-test $Argument wrote startup-crash.log."
  }
  Write-Host "PASS: $Argument" -ForegroundColor Green
}

Invoke-MizanSelfTest '--self-test-login'
Invoke-MizanSelfTest '--self-test-login-upgrade'
Invoke-MizanSelfTest '--self-test-legacy-login'
Invoke-MizanSelfTest '--self-test-login-recovery'
Invoke-MizanSelfTest '--self-test-views'

# Reuse the initialized database and confirm normal UI startup.
Write-Host 'Starting normal login window...' -ForegroundColor Cyan
$p = Start-Process -FilePath $Exe -PassThru
try {
  Start-Sleep -Seconds 7
  $crashLog = Join-Path $AppDir 'startup-crash.log'
  if (Test-Path $crashLog) {
    Get-Content $crashLog -Raw
    throw 'Normal startup created a crash log.'
  }
  if ($p.HasExited) {
    throw "Mizan.exe exited during normal startup with code $($p.ExitCode)."
  }
  Write-Host "PASS: normal first-run startup. PID=$($p.Id)" -ForegroundColor Green
}
finally {
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
