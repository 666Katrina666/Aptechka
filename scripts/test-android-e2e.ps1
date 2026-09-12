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
$AppiumHome = Join-Path $AppiumRoot '.appium-home'
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

function Assert-NodeVersion([string]$nodePath) {
    $raw = (& $nodePath -v).Trim().TrimStart('v')
    $version = [version]$raw
    $minimum = [version]'20.19.0'
    if ($version -lt $minimum) {
        throw @"
Appium 3 requires Node.js $minimum or newer. Found v$raw.
Install Node.js 20.19+ (or 22 LTS) and reopen the terminal.
"@
    }
}

function Get-AdbDevices([string]$adbPath) {
    $lines = @(& $adbPath devices)
    $devices = foreach ($line in $lines) {
        if ($line -match '^(?<id>\S+)\s+device$') {
            $Matches.id
        }
    }
    return @($devices)
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
    $npm = Get-Tool 'npm' @()
    if (-not $dotnet) { throw 'dotnet was not found on PATH.' }
    if (-not $node) { throw 'node was not found on PATH. Install Node.js 20.19+.' }
    if (-not $npm) { throw 'npm was not found on PATH. Install npm 10+ with Node.js.' }
    $npx = Get-Tool 'npx' @(Join-Path (Split-Path $npm) 'npx.cmd')
    if (-not $npx) { throw 'npx was not found on PATH.' }
    Assert-NodeVersion $node

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

    Write-Step 'Checking project-local Appium'
    $lockFile = Join-Path $AppiumRoot 'package-lock.json'
    if (-not (Test-Path -LiteralPath (Join-Path $AppiumRoot 'node_modules\appium\package.json'))) {
        Push-Location $AppiumRoot
        try {
            if (Test-Path -LiteralPath $lockFile) {
                & $npm ci
            }
            else {
                & $npm install
            }

            if ($LASTEXITCODE -ne 0) {
                throw "npm install in tools/appium failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }

    $env:APPIUM_HOME = $AppiumHome
    $driverSrc = Join-Path $AppiumRoot 'node_modules\appium-uiautomator2-driver'
    $driverDst = Join-Path $AppiumHome 'node_modules\appium-uiautomator2-driver\package.json'
    if (-not (Test-Path -LiteralPath $driverDst)) {
        Write-Step 'Registering project-local UiAutomator2 driver'
        Push-Location $AppiumRoot
        try {
            & $npx --no-install appium driver install --source local $driverSrc
            if ($LASTEXITCODE -ne 0) {
                throw "Appium driver install failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }

    Write-Step 'Running UiAutomator2 doctor'
    Push-Location $AppiumRoot
    try {
        & $npx --no-install appium driver doctor uiautomator2
        if ($LASTEXITCODE -ne 0) {
            throw "appium driver doctor uiautomator2 failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    Write-Step 'Selecting Android device'
    $unauthorized = @(& $adb devices) | Where-Object { $_ -match '\tunauthorized$' }
    if ($unauthorized.Count -gt 0) {
        throw 'adb reports an unauthorized device. Unlock the phone and accept the USB debugging prompt.'
    }

    $devices = Get-AdbDevices $adb
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

    if (Test-TcpPort 4723) {
        throw 'Appium port 4723 is already in use. Stop the other Appium server; this runner will not kill it.'
    }

    Write-Step 'Starting project-local Appium'
    $appiumEntry = Join-Path $AppiumRoot 'node_modules\appium\index.js'
    if (-not (Test-Path -LiteralPath $appiumEntry)) {
        $appiumEntry = Join-Path $AppiumRoot 'node_modules\appium\build\lib\main.js'
    }

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

    Write-Step 'Running canary E2E'
    $env:APTECHKA_E2E_APPIUM_URL = 'http://127.0.0.1:4723'
    $env:APTECHKA_E2E_DEVICE_ID = $DeviceId
    $env:APTECHKA_E2E_APK_PATH = $apkPath
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

Write-Host "E2E canary passed on $DeviceId. Logs: $ArtifactDir"
exit 0
