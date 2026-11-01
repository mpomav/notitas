# Compiles Notitas and leaves the single portable exe in dist\
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

dotnet build (Join-Path $root 'Notitas.csproj') -c Release -nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $root 'bin\Release\net48\Notitas.exe') $dist -Force
Get-Item (Join-Path $dist 'Notitas.exe') | Select-Object Name, Length
