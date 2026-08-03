[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDirectory = Join-Path $projectRoot "release"
$installerProject = Join-Path $projectRoot "installer\LanternControl.wixproj"
$installerVerification = Join-Path $projectRoot "scripts\verify-msi-installer.ps1"
$projectFile = Join-Path $projectRoot "src\Lantern.App\Lantern.App.csproj"
$solutionFile = Join-Path $projectRoot "LanternControl.slnx"
$releaseVersion = "0.1.3"

dotnet test $solutionFile -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed."
}

& $installerVerification -ProjectPath $installerProject
if ($LASTEXITCODE -ne 0) {
    throw "Installer definition verification failed."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

dotnet publish $projectFile `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

$publishedExe = Join-Path $publishDirectory "LANtern Control.exe"
Write-Host "Published: $publishedExe"
$portableArchive = Join-Path $projectRoot "outputs\LANtern-Control-v$releaseVersion-win-x64.zip"
if (Test-Path -LiteralPath $portableArchive) {
    Remove-Item -LiteralPath $portableArchive -Force
}
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $portableArchive -CompressionLevel Optimal
Write-Host "Portable: $portableArchive"

dotnet build $installerProject `
    -c $Configuration `
    -p:InstallerPlatform=x64 `
    -p:PublishDir="$publishDirectory"
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed."
}

$builtInstaller = Join-Path $projectRoot "packaging\wix\LANtern-Control-Setup-v$releaseVersion.msi"
$installer = Join-Path $projectRoot "outputs\LANtern-Control-Setup-v$releaseVersion.msi"
Copy-Item -LiteralPath $builtInstaller -Destination $installer -Force
Write-Host "Installer: $installer"
