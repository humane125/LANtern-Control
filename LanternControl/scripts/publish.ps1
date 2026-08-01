[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDirectory = Join-Path $projectRoot "release"
$projectFile = Join-Path $projectRoot "src\Lantern.App\Lantern.App.csproj"
$solutionFile = Join-Path $projectRoot "LanternControl.slnx"

dotnet test $solutionFile -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed."
}

dotnet publish $projectFile `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

$publishedExe = Join-Path $publishDirectory "LANtern Control.exe"
Write-Host "Published: $publishedExe"
