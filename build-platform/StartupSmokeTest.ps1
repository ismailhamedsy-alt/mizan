$ErrorActionPreference = 'Stop'
$Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop\mizan\dist\Mizan.exe'
if (-not (Test-Path $Exe)) { throw "Mizan.exe not found: $Exe" }

$p = Start-Process -FilePath $Exe -PassThru
try {
  Start-Sleep -Seconds 5
  if ($p.HasExited -and $p.ExitCode -ne 0) {
    throw "Mizan.exe exited during startup with code $($p.ExitCode)."
  }
  Write-Host "Startup smoke test passed. PID=$($p.Id)" -ForegroundColor Green
}
finally {
  if (-not $p.HasExited) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
  }
}
