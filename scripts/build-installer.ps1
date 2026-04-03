param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "",
    [string]$TimestampUrl = "https://timestamp.digicert.com",
    [switch]$RequireSigning
)

$ErrorActionPreference = "Stop"

function Get-SignToolPath {
    $explicit = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($explicit) {
        return $explicit.Source
    }

    $sdkRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    )

    foreach ($root in $sdkRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $candidate = Get-ChildItem -Path $root -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

function Invoke-CodeSign {
    param(
        [string]$SignTool,
        [string]$FilePath,
        [string]$CertPath,
        [string]$CertPassword,
        [string]$TsUrl
    )

    if (-not (Test-Path $FilePath)) {
        throw "Cannot sign missing file: $FilePath"
    }

    $signArgs = @("sign", "/fd", "SHA256", "/f", $CertPath, "/tr", $TsUrl, "/td", "SHA256")
    if (-not [string]::IsNullOrWhiteSpace($CertPassword)) {
        $signArgs += @("/p", $CertPassword)
    }
    $signArgs += $FilePath

    & $SignTool @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Code signing failed for: $FilePath"
    }
}

Write-Host "Publishing PrintEase application..."
dotnet publish ".\PrintEase.App\PrintEase.App.csproj" -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true

$publishDir = Join-Path $PSScriptRoot "..\PrintEase.App\bin\$Configuration\net8.0-windows\$Runtime\publish"
if (-not (Test-Path $publishDir)) {
    throw "Publish output not found: $publishDir"
}

$appExePath = Join-Path $publishDir "PrintEase.App.exe"

if ($RequireSigning -and [string]::IsNullOrWhiteSpace($CertificatePath)) {
    throw "Signing is required, but no certificate path was provided."
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path $CertificatePath)) {
        throw "Certificate file not found: $CertificatePath"
    }

    $signTool = Get-SignToolPath
    if (-not $signTool) {
        throw "signtool.exe not found. Install Windows SDK signing tools to sign artifacts."
    }

    Write-Host "Signing application binary..."
    Invoke-CodeSign -SignTool $signTool -FilePath $appExePath -CertPath $CertificatePath -CertPassword $CertificatePassword -TsUrl $TimestampUrl

    $appSignature = Get-AuthenticodeSignature $appExePath
    if ($appSignature.Status -ne "Valid") {
        throw "Application signature is not valid: $($appSignature.Status)"
    }
}

$innoFromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$innoCandidates = @(
    $innoFromPath.Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

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

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    Write-Host "Signing installer..."
    Invoke-CodeSign -SignTool $signTool -FilePath $installerPath -CertPath $CertificatePath -CertPassword $CertificatePassword -TsUrl $TimestampUrl

    $installerSignature = Get-AuthenticodeSignature $installerPath
    if ($installerSignature.Status -ne "Valid") {
        throw "Installer signature is not valid: $($installerSignature.Status)"
    }
}
elseif ($RequireSigning) {
    throw "Signing is required, but certificate information was not provided."
}

Write-Host "Installer created: $installerPath"
