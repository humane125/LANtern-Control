[CmdletBinding()]
param(
    [string]$ScriptPath
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
    $ScriptPath = Join-Path $PSScriptRoot "..\installer\LanternControl.iss"
}

$content = (Get-Content -LiteralPath $ScriptPath -Raw) -replace "`r`n", "`n"
$requirements = [ordered]@{
    "installation directory page" = '(?m)^DisableDirPage=no$'
    "shortcut task section" = '(?m)^\[Tasks\]$'
    "Start Menu shortcut choice" = '(?m)^Name: startmenu;.*$'
    "desktop shortcut choice" = '(?m)^Name: desktopicon;.*Flags: unchecked.*$'
    "Start Menu task binding" = '(?m)^Name: "\{autoprograms\}\\LANtern Control";.*Tasks: startmenu$'
    "desktop task binding" = '(?m)^Name: "\{autodesktop\}\\LANtern Control";.*Tasks: desktopicon$'
    "elevated shell launch" = '(?m)^Filename: "\{app\}\\\{#MyAppExeName\}";.*Verb: "runas";.*Flags:.*shellexec.*$'
}

foreach ($requirement in $requirements.GetEnumerator()) {
    if ($content -notmatch $requirement.Value) {
        throw "Installer definition is missing $($requirement.Key)."
    }
}

Write-Host "Installer definition verified: folder selection, optional shortcuts, and elevated launch."
