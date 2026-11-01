# Builds the app and runs the in-process UI tests in a throwaway folder.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $PSScriptRoot 'UiTests\UiTests.csproj') -c Release -nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$sandbox = Join-Path $env:TEMP 'notitas-uitests'
if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }
New-Item -ItemType Directory $sandbox | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'UiTests\bin\Release\net48\*') $sandbox -Force

Push-Location $sandbox
try {
    foreach ($stage in 'first', 'second') {
        "--- $stage"
        & .\NotitasUiTests.exe $stage
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
finally { Pop-Location }
