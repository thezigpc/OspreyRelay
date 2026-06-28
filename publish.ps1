#!/usr/bin/env pwsh
# Builds both Osprey Relay products as framework-dependent single-file executables for Windows x64.
# Requires .NET 10 Desktop Runtime on the target machine.
# Output: .\publish\OspreyRelay365.exe
#         .\publish\OspreyRelayWorkspace.exe
#         .\publish\check-runtime.ps1

$ErrorActionPreference = "Stop"
$ok = $true

Write-Host ""
Write-Host "=== Osprey Relay for M365 ===" -ForegroundColor Cyan
dotnet publish src\OspreyRelay.App\OspreyRelay.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=embedded `
  -o publish

if ($LASTEXITCODE -ne 0) { $ok = $false; Write-Error "M365 publish failed." }

Write-Host ""
Write-Host "=== Osprey Relay for Workspace ===" -ForegroundColor Cyan
dotnet publish src\OspreyRelay.WorkspaceApp\OspreyRelay.WorkspaceApp.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=embedded `
  -o publish

if ($LASTEXITCODE -ne 0) { $ok = $false; Write-Error "Workspace publish failed." }

if ($ok) {
    if (Test-Path "check-runtime.ps1") {
        Copy-Item "check-runtime.ps1" "publish\check-runtime.ps1" -Force
    }

    Write-Host ""
    Write-Host "Published artifacts:" -ForegroundColor Green
    foreach ($name in @("OspreyRelay365.exe", "OspreyRelayWorkspace.exe")) {
        $f = Get-Item "publish\$name" -ErrorAction SilentlyContinue
        if ($f) {
            Write-Host "  $($f.Name)   $([math]::Round($f.Length/1MB,1)) MB"
        }
    }
    Write-Host ""
    Write-Host "NOTE: Target machine needs .NET 10 Desktop Runtime." -ForegroundColor Yellow
    Write-Host "      Run check-runtime.ps1 on the target to verify."
}
