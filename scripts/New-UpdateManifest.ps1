[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $WindowsVersion,

    [Parameter()]
    [string] $WindowsAssetPath,

    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $LinuxVersion,

    [Parameter()]
    [string] $LinuxAssetPath,

    [Parameter()]
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\outputs\lantern-update-manifest.json')
)

$ErrorActionPreference = 'Stop'

function Add-PlatformEntry {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Platforms,

        [Parameter(Mandatory)]
        [string] $Platform,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $AssetPath
    )

    $resolvedAsset = Resolve-Path -LiteralPath $AssetPath -ErrorAction Stop
    $asset = Get-Item -LiteralPath $resolvedAsset.Path
    if ($asset.PSIsContainer) {
        throw "Update asset is a directory: $($asset.FullName)"
    }

    $Platforms[$Platform] = [ordered]@{
        version = $Version
        asset = $asset.Name
        sha256 = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

if ([string]::IsNullOrWhiteSpace($WindowsVersion) -ne
    [string]::IsNullOrWhiteSpace($WindowsAssetPath)) {
    throw 'WindowsVersion and WindowsAssetPath must be supplied together.'
}

if ([string]::IsNullOrWhiteSpace($LinuxVersion) -ne
    [string]::IsNullOrWhiteSpace($LinuxAssetPath)) {
    throw 'LinuxVersion and LinuxAssetPath must be supplied together.'
}

$platforms = [ordered]@{}
if (-not [string]::IsNullOrWhiteSpace($WindowsVersion)) {
    Add-PlatformEntry $platforms 'windows-x64' $WindowsVersion $WindowsAssetPath
}

if (-not [string]::IsNullOrWhiteSpace($LinuxVersion)) {
    Add-PlatformEntry $platforms 'linux-x64' $LinuxVersion $LinuxAssetPath
}

if ($platforms.Count -eq 0) {
    throw 'At least one platform version and asset must be supplied.'
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$manifestJson = [ordered]@{
    schemaVersion = 1
    platforms = $platforms
} | ConvertTo-Json -Depth 5
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($resolvedOutput, $manifestJson, $utf8WithoutBom)

Write-Output $resolvedOutput
