[CmdletBinding()]
param(
    [string]$DeviceId,
    [switch]$NoBuild,
    [switch]$KeepE2EData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ProductionPackage = 'io.github.vakineti.aptechka'
$E2EPackage = 'io.github.vakineti.aptechka.e2e'
$RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$AppiumRoot = Join-Path $RepositoryRoot 'tools\appium'
$ProjectPath = Join-Path $RepositoryRoot 'src\Aptechka.App\Aptechka.App.csproj'
$TestProject = Join-Path $RepositoryRoot 'tests\Aptechka.Android.E2E.Tests\Aptechka.Android.E2E.Tests.csproj'
$ArtifactDir = Join-Path $RepositoryRoot 'artifacts\e2e\android'
$DefaultApk = Join-Path $RepositoryRoot 'artifacts\e2e\android\apk\io.github.vakineti.aptechka.e2e-Signed.apk'
$script:AppiumProcess = $null
$script:Failed = $false

New-Item -ItemType Directory -Path $ArtifactDir -Force | Out-Null

function Write-Step([string]$message) {
    Write-Host "==> $message"
}

function Get-Tool([string]$name, [string[]]$candidates) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    return $null
}

function Get-ToolVersion([string]$toolPath, [string]$argument = '-v') {
    $raw = (& $toolPath $argument 2>&1 | Out-String).Trim()
    if ($raw -notmatch '(?<version>\d+\.\d+\.\d+)') {
        throw "Could not parse a version from '$toolPath $argument': $raw"
    }

    return [version]$Matches.version
}

function Test-NodeEngineRange([version]$version) {
    ($version.Major -eq 20 -and $version -ge [version]'20.19.0') -or
    ($version.Major -eq 22 -and $version -ge [version]'22.12.0') -or
    ($version.Major -ge 24)
}

function Assert-NodeVersion([string]$nodePath) {
    $version = Get-ToolVersion $nodePath
    $required = '^20.19.0 || ^22.12.0 || >=24.0.0'
    if (-not (Test-NodeEngineRange $version)) {
        throw "Node.js v$version is not supported. Required: $required."
    }
}

function Assert-NpmVersion([string]$npmPath) {
    $version = Get-ToolVersion $npmPath
    $required = '>=10'
    if ($version.Major -lt 10) {
        throw "npm $version is not supported. Required: $required."
    }
}

function Get-AdbDevices([string]$adbPath) {
    $ids = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(& $adbPath devices)) {
        if ($line -match '^(?<id>\S+)\s+device$') {
            $ids.Add($Matches.id)
        }
    }

    return $ids.ToArray()
}

function Get-ApkPackageId([string]$aaptPath, [string]$apkPath) {
    $output = & $aaptPath dump badging $apkPath
    foreach ($line in $output) {
        if ($line -match "^package: name='([^']+)'") {
            return $Matches[1]
        }
    }

    throw "Could not read package name from '$apkPath'."
}

function Test-TcpPort([int]$port) {
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $client.Connect('127.0.0.1', $port)
        $client.Close()
        return $true
    }
    catch {
        return $false
    }
}

function Wait-AppiumReady([uri]$url, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $status = [uri]::new($url, 'status')
    do {
        try {
            $response = Invoke-WebRequest -Uri $status -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200 -and $response.Content -match '"ready"\s*:\s*true') {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)

    throw "Appium did not become ready at $status. See artifacts/e2e/android/appium.log."
}

function Clear-AppiumHomeFromProcess {
    if (Test-Path Env:APPIUM_HOME) {
        Remove-Item Env:APPIUM_HOME
    }
}

function ConvertFrom-AppiumJson([object]$raw) {
    $text = ($raw | Out-String)
    $start = $text.IndexOf('{')
    $end = $text.LastIndexOf('}')
    if ($start -lt 0 -or $end -le $start) {
        throw "Appium did not return JSON. Output: $text"
    }

    return $text.Substring($start, $end - $start + 1) | ConvertFrom-Json
}

function Invoke-ProjectAppium {
    param(
        [Parameter(Mandatory)]
        [string]$NpxPath,
        [Parameter(Mandatory)]
        [string[]]$AppiumArgs,
        [switch]$Capture
    )

    Clear-AppiumHomeFromProcess
    Push-Location $AppiumRoot
    $previousError = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($Capture) {
            $output = & $NpxPath --no-install appium @AppiumArgs 2>&1 | ForEach-Object { "$_" }
            return [pscustomobject]@{
                ExitCode = $LASTEXITCODE
                Output   = $output
            }
        }

        & $NpxPath --no-install appium @AppiumArgs
        if ($LASTEXITCODE -ne 0) {
            throw "appium $($AppiumArgs -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $ErrorActionPreference = $previousError
        Pop-Location
    }
}

function Assert-ProjectUiAutomator2([string]$npxPath) {
    $result = Invoke-ProjectAppium -NpxPath $npxPath -AppiumArgs @('driver', 'list', '--installed', '--json') -Capture
    if ($result.ExitCode -ne 0) {
        throw "appium driver list --installed --json failed with exit code $($result.ExitCode). $($result.Output)"
    }

    $drivers = ConvertFrom-AppiumJson $result.Output
    $driver = $drivers.uiautomator2
    $requiredVersion = '8.1.1'
    $expectedPath = [IO.Path]::GetFullPath((Join-Path $AppiumRoot 'node_modules\appium-uiautomator2-driver'))
    if (-not $driver -or -not $driver.installed) {
        throw "Project-local UiAutomator2 was not found. Required: uiautomator2 $requiredVersion from $expectedPath."
    }

    $foundVersion = [string]$driver.version
    $foundPath = if ($driver.installPath) { [IO.Path]::GetFullPath([string]$driver.installPath) } else { '' }
    $samePath = [string]::Equals(
        $foundPath.TrimEnd('\'),
        $expectedPath.TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase)
    if ($foundVersion -ne $requiredVersion -or -not $samePath) {
        throw "Found uiautomator2 $foundVersion at '$foundPath'. Required: $requiredVersion from '$expectedPath'."
    }

    Write-Host "Found uiautomator2 $foundVersion from $expectedPath"
}

function Assert-UiAutomator2Doctor([string]$npxPath) {
    $result = Invoke-ProjectAppium -NpxPath $npxPath -AppiumArgs @('driver', 'doctor', 'uiautomator2') -Capture
    $text = ($result.Output | Out-String)
    Write-Host $text
    if ($result.ExitCode -eq 0) {
        return
    }

    $emulatorMissing = $text -match 'emulator could NOT be found'
    $onlyOneRequired = $text -match ',\s*1 required fix needed'
    if ($emulatorMissing -and $onlyOneRequired) {
        Write-Host 'UiAutomator2 doctor requires emulator.exe; this runner uses a physical device and will continue.'
        return
    }

    throw "appium driver doctor uiautomator2 failed with exit code $($result.ExitCode)."
}

function Stop-StartedAppium {
    if ($null -eq $script:AppiumProcess) {
        return
    }

    $rootId = $script:AppiumProcess.Id
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$rootId" -ErrorAction SilentlyContinue)
    foreach ($child in $children) {
        Stop-Process -Id $child.ProcessId -Force -ErrorAction SilentlyContinue
    }

    Stop-Process -Id $rootId -Force -ErrorAction SilentlyContinue
    $script:AppiumProcess = $null
}

try {
    Write-Step 'Checking tools'
    $dotnet = Get-Tool 'dotnet' @()
    $node = Get-Tool 'node' @()
    if (-not $dotnet) { throw 'dotnet was not found on PATH.' }
    if (-not $node) { throw 'node was not found on PATH. Required: ^20.19.0 || ^22.12.0 || >=24.0.0.' }
    Assert-NodeVersion $node

    $npm = Get-Tool 'npm' @()
    if (-not $npm) { throw 'npm was not found on PATH. Required: >=10.' }
    Assert-NpmVersion $npm
    $npx = Get-Tool 'npx' @(Join-Path (Split-Path $npm) 'npx.cmd')
    if (-not $npx) { throw 'npx was not found on PATH.' }

    if (-not $env:ANDROID_HOME -and (Test-Path 'C:\Program Files (x86)\Android\android-sdk')) {
        $env:ANDROID_HOME = 'C:\Program Files (x86)\Android\android-sdk'
    }

    if (-not $env:JAVA_HOME -and (Test-Path 'C:\Program Files\Android\Android Studio\jbr')) {
        $env:JAVA_HOME = 'C:\Program Files\Android\Android Studio\jbr'
    }

    if (-not $env:ANDROID_HOME) {
        throw 'ANDROID_HOME is not set and the default Android SDK path was not found.'
    }

    if (-not $env:JAVA_HOME) {
        throw 'JAVA_HOME is not set. Point it at a JDK 17+ (Android Studio JBR is fine).'
    }

    $adb = Get-Tool 'adb' @(Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe')
    $java = Get-Tool 'java' @(Join-Path $env:JAVA_HOME 'bin\java.exe')
    if (-not $adb) { throw "adb was not found. Install platform-tools in '$($env:ANDROID_HOME)'." }
    if (-not $java) { throw 'java was not found. Install a JDK 17+ and set JAVA_HOME.' }

    $aapt = Get-ChildItem (Join-Path $env:ANDROID_HOME 'build-tools') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'aapt.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if (-not $aapt) {
        throw "aapt.exe was not found under '$($env:ANDROID_HOME)\build-tools'. Install Android SDK build-tools."
    }

    if (Test-TcpPort 4723) {
        throw 'Appium port 4723 is already in use. Stop the other Appium server; this runner will not kill it.'
    }

    Write-Step 'Checking project-local Appium'
    $lockFile = Join-Path $AppiumRoot 'package-lock.json'
    if (-not (Test-Path -LiteralPath $lockFile)) {
        throw 'tools/appium/package-lock.json is required. The runner only uses npm ci.'
    }

    $appiumPkg = Join-Path $AppiumRoot 'node_modules\appium\package.json'
    $driverPkg = Join-Path $AppiumRoot 'node_modules\appium-uiautomator2-driver\package.json'
    if (-not (Test-Path -LiteralPath $appiumPkg) -or -not (Test-Path -LiteralPath $driverPkg)) {
        Push-Location $AppiumRoot
        try {
            & $npm ci
            if ($LASTEXITCODE -ne 0) {
                throw "npm ci in tools/appium failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }

    Clear-AppiumHomeFromProcess

    Write-Step 'Verifying npm-managed UiAutomator2'
    Assert-ProjectUiAutomator2 $npx

    Write-Step 'Running UiAutomator2 doctor'
    Assert-UiAutomator2Doctor $npx

    Write-Step 'Selecting Android device'
    $unauthorized = @((& $adb devices) | Where-Object { $_ -match '\tunauthorized$' })
    if ($unauthorized.Count -gt 0) {
        throw 'adb reports an unauthorized device. Unlock the phone and accept the USB debugging prompt.'
    }

    $devices = @(Get-AdbDevices $adb)
    if ($devices.Count -eq 0) {
        throw 'No Android device is connected. Enable USB debugging and run adb devices until the status is "device".'
    }

    if (-not $DeviceId) {
        $DeviceId = $env:APTECHKA_E2E_DEVICE_ID
    }

    if ($devices.Count -gt 1 -and -not $DeviceId) {
        throw @"
Several Android devices are connected: $($devices -join ', ').
Re-run with -DeviceId <adb-id>. The script will not install or clear anything until a device is chosen.
"@
    }

    if (-not $DeviceId) {
        $DeviceId = $devices[0]
    }

    if ($devices -notcontains $DeviceId) {
        throw "Device '$DeviceId' is not in adb devices ($($devices -join ', '))."
    }

    $apkPath = $DefaultApk
    if (-not $NoBuild) {
        Write-Step 'Building E2E Debug APK'
        $e2eOut = Join-Path $RepositoryRoot 'artifacts\e2e\android\apk\'
        $e2eObj = Join-Path $RepositoryRoot 'artifacts\e2e\android\obj\'
        & $dotnet build $ProjectPath -c Debug -f net9.0-android --nologo `
            -p:E2E=true `
            -p:ApplicationId=io.github.vakineti.aptechka.e2e `
            "-p:ApplicationTitle=Аптечка E2E" `
            "-p:OutputPath=$e2eOut" `
            "-p:IntermediateOutputPath=$e2eObj"
        if ($LASTEXITCODE -ne 0) {
            throw "E2E Android build failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
        throw "E2E APK was not found at '$apkPath'."
    }

    Write-Step 'Verifying E2E package id'
    $packageId = Get-ApkPackageId $aapt $apkPath
    if ($packageId -ne $E2EPackage) {
        throw "Refusing to install: APK package is '$packageId', expected '$E2EPackage'."
    }

    if ($packageId -eq $ProductionPackage) {
        throw 'Refusing to continue: production package id was selected.'
    }

    Write-Step "Installing $E2EPackage on $DeviceId"
    if (-not $KeepE2EData) {
        $installed = & $adb -s $DeviceId shell pm path $E2EPackage 2>$null
        if ($LASTEXITCODE -eq 0 -and "$installed".Trim().Length -gt 0) {
            Write-Host "Clearing data for $E2EPackage only"
            & $adb -s $DeviceId shell pm clear io.github.vakineti.aptechka.e2e
            if ($LASTEXITCODE -ne 0) {
                throw "pm clear for $E2EPackage failed with exit code $LASTEXITCODE."
            }
        }
    }

    & $adb -s $DeviceId install -r $apkPath
    if ($LASTEXITCODE -ne 0) {
        throw "adb install -r of the E2E APK failed with exit code $LASTEXITCODE."
    }

    Write-Step 'Starting project-local Appium'
    $appiumEntry = Join-Path $AppiumRoot 'node_modules\appium\index.js'
    if (-not (Test-Path -LiteralPath $appiumEntry)) {
        $appiumEntry = Join-Path $AppiumRoot 'node_modules\appium\build\lib\main.js'
    }

    Clear-AppiumHomeFromProcess
    $appiumLog = Join-Path $ArtifactDir 'appium.log'
    $appiumErr = Join-Path $ArtifactDir 'appium.err.log'
    $script:AppiumProcess = Start-Process -FilePath $node -ArgumentList @(
        $appiumEntry,
        '--address', '127.0.0.1',
        '--port', '4723',
        '--use-drivers', 'uiautomator2',
        '--log', $appiumLog
    ) -PassThru -WindowStyle Hidden -WorkingDirectory $AppiumRoot -RedirectStandardError $appiumErr
    Wait-AppiumReady ([uri]'http://127.0.0.1:4723/') 60

    $env:APTECHKA_E2E_APPIUM_URL = 'http://127.0.0.1:4723'
    $env:APTECHKA_E2E_DEVICE_ID = $DeviceId
    $env:APTECHKA_E2E_APK_PATH = $apkPath
    $env:APTECHKA_E2E_ARTIFACT_DIR = $ArtifactDir
    Write-Step 'Running E2E tests'
    & $dotnet test $TestProject --nologo --logger "trx;LogFileName=android-e2e.trx" --results-directory $ArtifactDir
    if ($LASTEXITCODE -ne 0) {
        throw "E2E tests failed with exit code $LASTEXITCODE."
    }
}
catch {
    $script:Failed = $true
    Write-Host $_ -ForegroundColor Red
}
finally {
    Stop-StartedAppium
}

if ($script:Failed) {
    exit 1
}

Write-Host "E2E tests passed on $DeviceId. Logs: $ArtifactDir"
exit 0
