<#
.SYNOPSIS
    Builds a release of PicRestore: a portable app folder (+ zip) and a Windows installer (Setup.exe).

.DESCRIPTION
    1. Runs the unit tests (skip with -SkipTests).
    2. Publishes PicRestore.App for Windows (x64 by default) into artifacts\publish\.
    3. Adds the LaMa AI repair model (~92 MB) next to the app so it works offline from the first run
       (skip with -NoAiModel; the app then downloads it on first restoration).
    4. Starts the published exe for a few seconds to make sure it launches (skip with -NoSmokeTest).
    5. Zips the folder as a portable build.
    6. Compiles installer\PicRestore.iss with Inno Setup 6 into artifacts\PicRestore-Setup-<version>-<arch>.exe.
       If Inno Setup isn't installed, steps 1-5 still complete and the script says how to install it.

    Modes:
      SelfContained       (default) .NET and the Windows App SDK are bundled. Nothing else to install.
      FrameworkDependent  .NET is bundled; Setup.exe installs the Windows App Runtime if it's missing.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\package.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\package.ps1 -Mode FrameworkDependent -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',

    [ValidateSet('SelfContained', 'FrameworkDependent')]
    [string]$Mode = 'SelfContained',

    [switch]$SkipTests,
    [switch]$NoAiModel,
    [switch]$NoSmokeTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest / Compress-Archive are far faster without it

$Root = Split-Path -Parent $PSScriptRoot
$AppProject = Join-Path $Root 'src\PicRestore.App\PicRestore.App.csproj'
$TestProject = Join-Path $Root 'src\PicRestore.Tests\PicRestore.Tests.csproj'
$IssScript = Join-Path $Root 'installer\PicRestore.iss'
$Artifacts = Join-Path $Root 'artifacts'
$Rid = "win-$Arch"
$ExeName = 'PicRestore.App.exe'

$LamaFile = 'inpainting_lama_2025jan.onnx'
$LamaUrl = 'https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/inpainting_lama/inpainting_lama_2025jan.onnx'
$LamaSha256 = '7df918ac3921d3daf0aae1d219776cf0dc4e4935f035af81841b40adcf74fdf2'

function Step([string]$message) { Write-Host ''; Write-Host "==> $message" -ForegroundColor Cyan }
function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments | Out-Host   # through the host so the transcript (package.log) records it
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}
function Format-Size([long]$bytes) { '{0:N1} MB' -f ($bytes / 1MB) }
function Find-Iscc {
    @(
        (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

# Everything below is also written to artifacts\package.log.
New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null
$LogPath = Join-Path $Artifacts 'package.log'
Start-Transcript -Path $LogPath -Force | Out-Null
trap {
    Write-Host ''
    Write-Host "BUILD FAILED: $_" -ForegroundColor Red
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 8 SDK is required (https://dotnet.microsoft.com/download/dotnet/8.0).'
}

$csprojText = Get-Content $AppProject -Raw
$Version = '1.0.0'
if ($csprojText -match '<Version>\s*([^<\s]+)\s*</Version>') { $Version = $Matches[1] }
$PublishDir = Join-Path $Artifacts "publish\PicRestore-$Version-$Rid"
Write-Host "PicRestore $Version  |  $Rid  |  $Mode" -ForegroundColor Green

# ---------------------------------------------------------------------------------------------- tests
if (-not $SkipTests) {
    Step 'Running unit tests'
    Invoke-Checked 'dotnet' @('test', $TestProject, '-c', 'Release', '--nologo', '-v', 'q')
}

# -------------------------------------------------------------------------------------------- publish
Step "Publishing the app to $PublishDir"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
$winAppSdkSelfContained = if ($Mode -eq 'SelfContained') { 'true' } else { 'false' }
Invoke-Checked 'dotnet' @(
    'publish', $AppProject,
    '-c', 'Release',
    '-r', $Rid,
    '--self-contained', 'true',
    "-p:WindowsAppSDKSelfContained=$winAppSdkSelfContained",
    '-p:PublishTrimmed=false',
    '-p:PublishSingleFile=false',
    '-p:PublishReadyToRun=false',
    '-o', $PublishDir,
    '--nologo')

$exePath = Join-Path $PublishDir $ExeName
if (-not (Test-Path $exePath)) { throw "Publish finished but $ExeName is missing from $PublishDir." }

# The app can't start without its compiled XAML and resource index (see CopyWinUIResourcesToPublish
# in the app's csproj). Check here so a missing file is reported by name, not as a crash code.
foreach ($required in @('PicRestore.App.pri', 'App.xbf', 'MainWindow.xbf', 'Views\AboutPage.xbf')) {
    if (-not (Test-Path (Join-Path $PublishDir $required))) {
        throw "Publish output is missing $required - the app would crash on launch."
    }
}

# ------------------------------------------------------------------------------------------- AI model
if (-not $NoAiModel) {
    Step 'Adding the LaMa AI repair model'
    $modelTarget = Join-Path $PublishDir "Models\$LamaFile"
    New-Item -ItemType Directory -Force -Path (Split-Path $modelTarget) | Out-Null

    # Reuse the copy the app already downloaded on this PC, else a cached one, else download it once.
    $cacheDir = Join-Path $Artifacts 'cache'
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "PicRestore\Models\$LamaFile"),
        (Join-Path $cacheDir $LamaFile))
    $source = $candidates | Where-Object { Test-Path $_ } |
        Where-Object { (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant() -eq $LamaSha256 } |
        Select-Object -First 1

    if (-not $source) {
        Write-Host 'Downloading the model once (~92 MB)...'
        New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
        $source = Join-Path $cacheDir $LamaFile
        try {
            Invoke-WebRequest -Uri $LamaUrl -OutFile $source -UseBasicParsing
            if ((Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $LamaSha256) {
                Remove-Item $source -Force
                throw 'checksum mismatch'
            }
        }
        catch {
            Write-Warning "Couldn't get the AI model ($($_.Exception.Message)). The build continues without it; the app will download it on its first restoration."
            $source = $null
        }
    }

    if ($source) {
        Copy-Item $source $modelTarget -Force
        Write-Host "Bundled $LamaFile"
    }
}

# ----------------------------------------------------------------------------------------- smoke test
if (-not $NoSmokeTest) {
    Step 'Smoke test: starting the published app for 12 seconds'
    $process = Start-Process -FilePath $exePath -WorkingDirectory $PublishDir -PassThru
    $null = $process.Handle   # keep the handle so ExitCode is readable if it crashes
    Start-Sleep -Seconds 12
    if ($process.HasExited) {
        $code = '0x{0:X8}' -f $process.ExitCode
        # Record what Windows logged about the crash, for diagnosis.
        try {
            Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-2) } -MaxEvents 30 -ErrorAction Stop |
                Where-Object { $_.Message -match 'PicRestore' } |
                Select-Object -First 3 |
                ForEach-Object { Write-Host "[$($_.ProviderName) $($_.Id)] $($_.Message)" }
        }
        catch { }
        $crashLog = Join-Path $env:LOCALAPPDATA 'PicRestore\crash.log'
        if (Test-Path $crashLog) { Get-Content $crashLog -Tail 20 | Out-Host }
        throw "The published app exited during startup (exit code $code). Details above. Try: build\package.ps1 -Mode FrameworkDependent"
    }
    Stop-Process -Id $process.Id -Force
    Write-Host 'The app started and stayed running.' -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------- portable zip
Step 'Creating the portable zip'
$zipPath = Join-Path $Artifacts "PicRestore-$Version-$Rid-portable.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

# ------------------------------------------------------------------------------------------- installer
Step 'Building the installer'
$iscc = Find-Iscc
if (-not $iscc -and (Get-Command winget -ErrorAction SilentlyContinue) -and [Environment]::UserInteractive) {
    Write-Host 'Inno Setup 6 (free, https://jrsoftware.org) is needed to build Setup.exe and is not installed.'
    $answer = Read-Host 'Install it now with winget? This accepts the Inno Setup license. [Y/n]'
    if ($answer -eq '' -or $answer -match '^[Yy]') {
        & winget install --id JRSoftware.InnoSetup -e --scope user --accept-package-agreements --accept-source-agreements | Out-Host
        if (-not (Find-Iscc)) {
            # Some winget manifests have no per-user installer; fall back to the default (may ask for admin).
            & winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements | Out-Host
        }
        $iscc = Find-Iscc
    }
}

$setupPath = $null
if (-not $iscc) {
    Write-Warning 'Inno Setup 6 is not installed, so only the portable build was made.'
    Write-Warning 'Install it with:  winget install --id JRSoftware.InnoSetup -e   then run this script again.'
}
else {
    $isccArgs = @(
        "/DAppVersion=$Version",
        "/DAppSource=$PublishDir",
        "/DOutputDir=$Artifacts",
        "/DArch=$Arch",
        '/Q')

    if ($Mode -eq 'FrameworkDependent') {
        # Setup.exe runs Microsoft's Windows App Runtime installer (a no-op when it's already present).
        if ($csprojText -notmatch 'Include="Microsoft\.WindowsAppSDK"\s+Version="(\d+)\.(\d+)') {
            throw 'Could not read the Microsoft.WindowsAppSDK version from the app project.'
        }
        $major = $Matches[1]
        $minor = $Matches[2]
        $runtimeInstaller = Join-Path $Artifacts "cache\WindowsAppRuntimeInstall-$Arch.exe"
        if (-not (Test-Path $runtimeInstaller)) {
            New-Item -ItemType Directory -Force -Path (Split-Path $runtimeInstaller) | Out-Null
            $runtimeUrl = "https://aka.ms/windowsappsdk/$major.$minor/latest/windowsappruntimeinstall-$Arch.exe"
            Write-Host "Downloading the Windows App Runtime installer from $runtimeUrl"
            Invoke-WebRequest -Uri $runtimeUrl -OutFile "$runtimeInstaller.part" -UseBasicParsing
            Move-Item "$runtimeInstaller.part" $runtimeInstaller -Force
        }
        $isccArgs += "/DRuntimeInstaller=$runtimeInstaller"
    }

    Invoke-Checked $iscc ($isccArgs + $IssScript)
    $setupPath = Join-Path $Artifacts "PicRestore-Setup-$Version-$Arch.exe"
}

# ------------------------------------------------------------------------------------------- summary
Step 'Done'
$folderSize = (Get-ChildItem $PublishDir -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Portable app : {0}  ({1})" -f $exePath, (Format-Size $folderSize))
Write-Host ("Portable zip : {0}  ({1})" -f $zipPath, (Format-Size (Get-Item $zipPath).Length))
if ($setupPath -and (Test-Path $setupPath)) {
    Write-Host ("Installer    : {0}  ({1})" -f $setupPath, (Format-Size (Get-Item $setupPath).Length)) -ForegroundColor Green
}
Stop-Transcript | Out-Null
