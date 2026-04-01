param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing PrintEase application..."
dotnet publish ".\PrintEase.App\PrintEase.App.csproj" -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true

$publishDir = Join-Path $PSScriptRoot "..\PrintEase.App\bin\$Configuration\net8.0-windows\$Runtime\publish"
if (-not (Test-Path $publishDir)) {
    throw "Publish output not found: $publishDir"
}

$innoCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$innoCompiler = $innoCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $innoCompiler) {
    throw "Inno Setup compiler not found. Install Inno Setup 6 and rerun."
}

Write-Host "Building installer with Inno Setup..."
& $innoCompiler ".\installer\PrintEase.iss"

$installerPath = ".\installer\Output\PrintEase-Setup.exe"
if (-not (Test-Path $installerPath)) {
    throw "Installer output not found: $installerPath"
}

Write-Host "Installer created: $installerPath"
