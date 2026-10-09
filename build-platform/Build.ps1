$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root 'desktop\mizan\MizanDesktop.csproj'
$Work = Join-Path $Root 'desktop\mizan'
$Dist = Join-Path $Work 'dist'
$Publish = Join-Path $Work 'publish\win-x64'
$Log = Join-Path $Dist 'build.log'

if (-not (Test-Path $Project)) { throw "Project not found: $Project" }
$dotnet = Get-Command dotnet -ErrorAction Stop
$sdks = & $dotnet.Source --list-sdks
$required = $sdks | Where-Object { $_ -match '^10\.0\.112\s' }
if (-not $required) { throw '.NET SDK 10.0.112 is required. Install it and rerun.' }

Remove-Item (Join-Path $Work 'bin') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $Work 'obj') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $Work 'publish') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $Dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Dist -ItemType Directory -Force | Out-Null

"=== MIZAN WINDOWS BUILD ===" | Set-Content $Log -Encoding utf8
& $dotnet.Source --version *>> $Log
if ($LASTEXITCODE -ne 0) { throw 'dotnet --version failed' }

Write-Host '[1/4] RESTORE' -ForegroundColor Cyan
& $dotnet.Source restore $Project --configfile (Join-Path $Work 'NuGet.Config') --disable-parallel *>> $Log
if ($LASTEXITCODE -ne 0) { Get-Content $Log; throw 'RESTORE FAILED' }

Write-Host '[2/4] BUILD RELEASE' -ForegroundColor Cyan
& $dotnet.Source build $Project -c Release --no-restore --nologo *>> $Log
if ($LASTEXITCODE -ne 0) { Get-Content $Log; throw 'BUILD FAILED' }

Write-Host '[3/4] PUBLISH WIN-X64 SELF-CONTAINED' -ForegroundColor Cyan
& $dotnet.Source publish $Project -c Release -r win-x64 --self-contained true --no-restore --nologo `
  '-p:PublishSingleFile=true' `
  '-p:IncludeNativeLibrariesForSelfExtract=true' `
  '-p:PublishTrimmed=false' `
  '-p:DebugType=None' `
  '-p:DebugSymbols=false' `
  '-p:EnableCompressionInSingleFile=true' `
  -o $Publish *>> $Log
if ($LASTEXITCODE -ne 0) { Get-Content $Log; throw 'PUBLISH FAILED' }

Write-Host '[4/4] VALIDATE EXE' -ForegroundColor Cyan
$Exe = Join-Path $Publish 'Mizan.exe'
if (-not (Test-Path $Exe)) { Get-Content $Log; throw "Mizan.exe was not generated: $Exe" }
$bytes = [System.IO.File]::ReadAllBytes($Exe)
if ($bytes.Length -lt 2 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw 'Mizan.exe is not a valid Windows PE file.' }
$sha = (Get-FileHash $Exe -Algorithm SHA256).Hash
Copy-Item $Exe (Join-Path $Dist 'Mizan.exe') -Force
@"
Mizan Windows Build Artifact
SDK: 10.0.112
Runtime: win-x64
Self-contained: true
EXE: $Exe
SHA256: $sha
Size: $($bytes.Length)
"@ | Set-Content (Join-Path $Dist 'BUILD-MANIFEST.txt') -Encoding utf8

Write-Host "SUCCESS: $Exe" -ForegroundColor Green
Write-Host "SHA256: $sha"
