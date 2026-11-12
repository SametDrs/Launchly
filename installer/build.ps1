# Launchly kurulum dosyasını üretir: artifacts\Launchly-Setup.exe
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish (Join-Path $root 'src\Launchly.App\Launchly.App.csproj') `
    -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish başarısız' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 bulunamadı: winget install JRSoftware.InnoSetup' }

& $iscc (Join-Path $PSScriptRoot 'Launchly.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC başarısız' }
