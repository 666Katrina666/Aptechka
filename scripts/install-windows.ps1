[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repositoryRoot 'src\Aptechka.App\Aptechka.App.csproj'
$publishPath = Join-Path $repositoryRoot 'artifacts\windows\win-x64'
$installPath = Join-Path $env:LOCALAPPDATA 'Programs\Aptechka'
$executablePath = Join-Path $installPath 'Aptechka.App.exe'
$desktopPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$shortcutPath = Join-Path $desktopPath 'Аптечка.lnk'

if (-not $SkipBuild) {
    & dotnet publish $projectPath `
        -f net9.0-windows10.0.19041.0 `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -p:WindowsPackageType=None `
        -o $publishPath

    if ($LASTEXITCODE -ne 0) {
        throw "Windows publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $publishPath 'Aptechka.App.exe') -PathType Leaf)) {
    throw "Published executable was not found at '$publishPath'."
}

$runningInstalledApp = Get-Process -Name 'Aptechka.App' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $executablePath }
if ($runningInstalledApp) {
    throw 'Close the installed Aptechka app before updating it.'
}

New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Copy-Item -Path (Join-Path $publishPath '*') -Destination $installPath -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executablePath
$shortcut.WorkingDirectory = $installPath
$shortcut.IconLocation = "$executablePath,0"
$shortcut.Description = 'Локальная домашняя аптечка'
$shortcut.Save()

Write-Host "Installed: $executablePath"
Write-Host "Shortcut: $shortcutPath"
