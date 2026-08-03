[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string]$PackagePath,
    [string]$AppProjectPath,
    [string]$PublishScriptPath
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot "..\installer\LanternControl.wixproj"
}

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $PSScriptRoot "..\installer\Package.wxs"
}

if ([string]::IsNullOrWhiteSpace($AppProjectPath)) {
    $AppProjectPath = Join-Path $PSScriptRoot "..\src\Lantern.App\Lantern.App.csproj"
}

if ([string]::IsNullOrWhiteSpace($PublishScriptPath)) {
    $PublishScriptPath = Join-Path $PSScriptRoot "publish.ps1"
}

foreach ($path in @($ProjectPath, $PackagePath, $AppProjectPath, $PublishScriptPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required MSI packaging file is missing: $path"
    }
}

$project = (Get-Content -LiteralPath $ProjectPath -Raw) -replace "`r`n", "`n"
$package = (Get-Content -LiteralPath $PackagePath -Raw) -replace "`r`n", "`n"
$appProject = (Get-Content -LiteralPath $AppProjectPath -Raw) -replace "`r`n", "`n"
$publish = (Get-Content -LiteralPath $PublishScriptPath -Raw) -replace "`r`n", "`n"

$requirements = [ordered]@{
    "WiX 5 SDK" = '<Project Sdk="WixToolset\.Sdk/5\.0\.2">'
    "intentional same-version ICE suppression" = '<SuppressIces>ICE61</SuppressIces>'
    "embedded MSI cabinet" = '<MediaTemplate[^>]*EmbedCab="yes"'
    "same-version replacement support" = '<MajorUpgrade[^>]*AllowSameVersionUpgrades="yes"'
    "published application files" = '<Files[^>]*Include="\$\(var\.PublishDir\)\\\*\*"'
    "install-folder and feature UI" = '<ui:WixUI[^>]*Id="WixUI_Mondo"[^>]*InstallDirectory="INSTALLFOLDER"'
    "Start Menu shortcut feature" = '<Feature[^>]*Id="StartMenuShortcutFeature"'
    "optional desktop shortcut feature" = '<Feature[^>]*Id="DesktopShortcutFeature"[^>]*Level="200"'
    "MSI build command" = 'dotnet build \$installerProject'
    "MSI release artifact" = 'LANtern-Control-Setup-v\$releaseVersion\.msi'
}

$packageVersion = [regex]::Match($package, '<Package[^>]*?\sVersion="([^"]+)"').Groups[1].Value
$projectVersion = [regex]::Match($project, '<OutputName>LANtern-Control-Setup-v([^<]+)</OutputName>').Groups[1].Value
$publishVersion = [regex]::Match($publish, '\$releaseVersion\s*=\s*"([^"]+)"').Groups[1].Value
$applicationVersion = [regex]::Match($appProject, '<Version>([^<]+)</Version>').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($packageVersion) -or
    $packageVersion -ne $projectVersion -or
    $packageVersion -ne $publishVersion -or
    $packageVersion -ne $applicationVersion) {
    throw "Installer versions are inconsistent: package=$packageVersion project=$projectVersion publish=$publishVersion app=$applicationVersion"
}

foreach ($requirement in $requirements.GetEnumerator()) {
    $content = if ($requirement.Key -in @("WiX 5 SDK", "intentional same-version ICE suppression")) {
        $project
    }
    elseif ($requirement.Key -in @("MSI build command", "MSI release artifact")) {
        $publish
    }
    else {
        $package
    }

    if ($content -notmatch $requirement.Value) {
        throw "MSI packaging is missing $($requirement.Key)."
    }
}

if ($publish -match 'ISCC\.exe|LanternControl\.iss') {
    throw "The release script still builds the heuristic-prone self-extracting Inno Setup executable."
}

if ($appProject -notmatch '<PublishSingleFile>false</PublishSingleFile>' -or
    $appProject -match '<(?:IncludeNativeLibrariesForSelfExtract|EnableCompressionInSingleFile)>true</') {
    throw "The application must remain an inspectable multi-file publish without compressed self-extraction."
}

if ($publish -notmatch 'Remove-Item -LiteralPath \$publishDirectory -Recurse -Force' -or
    $publish -notmatch 'LANtern-Control-v[^"\r\n]*-win-x64\.zip') {
    throw "The publish script must clean stale files and create the portable multi-file ZIP."
}

Write-Host "MSI packaging verified: standard package, inspectable payload, folder selection, and optional shortcuts."
