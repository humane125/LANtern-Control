[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDirectory = Join-Path $projectRoot "release"
$installerScript = Join-Path $projectRoot "installer\LanternControl.iss"
$installerVerification = Join-Path $projectRoot "scripts\verify-installer-script.ps1"
$projectFile = Join-Path $projectRoot "src\Lantern.App\Lantern.App.csproj"
$solutionFile = Join-Path $projectRoot "LanternControl.slnx"

dotnet test $solutionFile -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed."
}

& $installerVerification -ScriptPath $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Installer definition verification failed."
}

dotnet publish $projectFile `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

$publishedExe = Join-Path $publishDirectory "LANtern Control.exe"
Write-Host "Published: $publishedExe"
$portableExe = Join-Path $projectRoot "outputs\LANtern-Control-v0.1.0.exe"
Copy-Item -LiteralPath $publishedExe -Destination $portableExe -Force
Write-Host "Portable: $portableExe"

$innoCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

$innoCompiler = $innoCandidates | Select-Object -First 1
if (-not $innoCompiler) {
    throw "Inno Setup 6 was not found. Install JRSoftware.InnoSetup with winget."
}

& $innoCompiler $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed."
}

$installer = Join-Path $projectRoot "outputs\LANtern-Control-Setup-v0.1.0.exe"
Write-Host "Installer: $installer"
