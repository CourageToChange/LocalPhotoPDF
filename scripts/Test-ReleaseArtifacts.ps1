[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.0.0',

    [string] $ArtifactRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'),

    [switch] $SkipInstallerLifecycle
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [string[]] $ArgumentList = @()
    )

    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "Process failed with exit code $($process.ExitCode): $FilePath $($ArgumentList -join ' ')"
    }
}

function Test-ApplicationWindow {
    param([Parameter(Mandatory)][string] $Executable)

    $process = Start-Process -FilePath $Executable -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
            if ($process.HasExited) {
                throw "LocalPhotoPDF exited before showing its window (exit code $($process.ExitCode))."
            }
        } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)

        if ($process.MainWindowHandle -eq 0 -or $process.MainWindowTitle -ne 'LocalPhotoPDF') {
            throw 'LocalPhotoPDF did not expose its expected main window within 20 seconds.'
        }

        if (-not $process.CloseMainWindow()) {
            throw 'LocalPhotoPDF did not accept a graceful main-window close request.'
        }

        if (-not $process.WaitForExit(10000)) {
            throw 'LocalPhotoPDF did not exit within 10 seconds after its window closed.'
        }

        if ($process.ExitCode -ne 0) {
            throw "LocalPhotoPDF exited with code $($process.ExitCode)."
        }
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            [void] $process.WaitForExit(5000)
        }

        $process.Dispose()
    }
}

function Test-OperationRefusedWhileAppRuns {
    param(
        [Parameter(Mandatory)][string] $ApplicationExecutable,
        [Parameter(Mandatory)][string] $OperationExecutable,
        [string[]] $OperationArguments = @(),
        [switch] $RequireNonZeroExit
    )

    $application = Start-Process -FilePath $ApplicationExecutable -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $application.Refresh()
            if ($application.HasExited) {
                throw 'LocalPhotoPDF exited before the running-app safety check.'
            }
        } while ($application.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)

        if ($application.MainWindowHandle -eq 0) {
            throw 'LocalPhotoPDF did not expose a window for the running-app safety check.'
        }

        $operation = Start-Process `
            -FilePath $OperationExecutable `
            -ArgumentList $OperationArguments `
            -PassThru `
            -Wait
        try {
            if ($RequireNonZeroExit -and $operation.ExitCode -eq 0) {
                throw "The guarded operation unexpectedly returned success while LocalPhotoPDF was running: $OperationExecutable"
            }
        }
        finally {
            $operation.Dispose()
        }

        $application.Refresh()
        if ($application.HasExited) {
            throw 'The guarded setup operation unexpectedly terminated the running app.'
        }
    }
    finally {
        if (-not $application.HasExited) {
            [void] $application.CloseMainWindow()
            if (-not $application.WaitForExit(10000)) {
                $application.Kill($true)
                [void] $application.WaitForExit(5000)
            }
        }

        $application.Dispose()
    }
}

$resolvedArtifacts = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$portablePath = Join-Path $resolvedArtifacts "LocalPhotoPDF-$Version-win-x64.zip"
$installerPath = Join-Path $resolvedArtifacts "LocalPhotoPDF-Setup-$Version-win-x64.exe"
$sbomPath = Join-Path $resolvedArtifacts "LocalPhotoPDF-$Version-sbom.cdx.json"
$checksumPath = Join-Path $resolvedArtifacts 'SHA256SUMS.txt'

foreach ($requiredFile in @($portablePath, $sbomPath, $checksumPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required release artifact is missing: $requiredFile"
    }
}
if (-not $SkipInstallerLifecycle -and -not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Required installer is missing: $installerPath"
}

$checksumLines = Get-Content -LiteralPath $checksumPath
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
        throw "Malformed checksum line: $line"
    }

    $expectedHash = $Matches[1]
    $releaseFile = Join-Path $resolvedArtifacts $Matches[2]
    if (-not (Test-Path -LiteralPath $releaseFile -PathType Leaf)) {
        throw "Checksummed release file is missing: $releaseFile"
    }

    $actualHash = (Get-FileHash -LiteralPath $releaseFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Checksum mismatch for $releaseFile"
    }
}

$sbom = Get-Content -LiteralPath $sbomPath -Raw | ConvertFrom-Json
$componentNames = @($sbom.components | ForEach-Object { $_.name })
foreach ($requiredComponent in @(
        'PDFsharp-WPF',
        'Microsoft.NETCore.App.Runtime.win-x64',
        'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    if ($requiredComponent -notin $componentNames) {
        throw "Runtime SBOM component is missing: $requiredComponent"
    }
}
if ($componentNames | Where-Object { $_ -match 'xunit|coverlet|Test\.Sdk' }) {
    throw 'The distributed-runtime SBOM incorrectly includes a test-only package.'
}

$smokeRoot = Join-Path $resolvedArtifacts 'smoke-test'
$artifactPrefix = $resolvedArtifacts.TrimEnd('\') + '\'
$resolvedSmokeCandidate = [IO.Path]::GetFullPath($smokeRoot)
if (-not $resolvedSmokeCandidate.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected smoke-test path: $resolvedSmokeCandidate"
}

if (Test-Path -LiteralPath $smokeRoot) {
    Remove-Item -LiteralPath $smokeRoot -Recurse -Force
}
[void] (New-Item -ItemType Directory -Path $smokeRoot)

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\LocalPhotoPDF'
$settingsDirectory = Join-Path $env:LOCALAPPDATA 'LocalPhotoPDF'
$uninstallRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalPhotoPDF'
$appRegistryPath = 'HKCU:\Software\LocalPhotoPDF'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\LocalPhotoPDF.lnk'
$settingsBackup = Join-Path $smokeRoot 'preexisting-settings'
$hadPreexistingSettings = Test-Path -LiteralPath $settingsDirectory -PathType Container
if ($hadPreexistingSettings) {
    Copy-Item -LiteralPath $settingsDirectory -Destination $settingsBackup -Recurse
}

try {
    Expand-Archive -LiteralPath $portablePath -DestinationPath $smokeRoot
    $portableExecutable = Join-Path $smokeRoot "LocalPhotoPDF-$Version-win-x64\LocalPhotoPDF.exe"
    if (-not (Test-Path -LiteralPath $portableExecutable -PathType Leaf)) {
        throw 'The portable ZIP does not contain LocalPhotoPDF.exe at the expected path.'
    }
    Test-ApplicationWindow -Executable $portableExecutable

    if (-not $SkipInstallerLifecycle) {
        if ((Test-Path -LiteralPath $installDirectory) -or
            (Test-Path -LiteralPath $uninstallRegistryPath) -or
            (Test-Path -LiteralPath $appRegistryPath)) {
            throw 'An existing LocalPhotoPDF installation was detected. Installer lifecycle smoke testing refused to overwrite it.'
        }

        # /FROMAPP is passed by the app's update button and makes the installer wait for the
        # single-instance mutex instead of aborting the instant it finds it held. Exercised here
        # because it had no coverage at all: not a unit test, and not this smoke test, which only
        # ever ran /S. An untested switch on the update path is exactly where a silent regression
        # would sit.
        Invoke-CheckedProcess -FilePath $installerPath -ArgumentList @('/S', '/FROMAPP')
        $installedExecutable = Join-Path $installDirectory 'LocalPhotoPDF.exe'
        $uninstaller = Join-Path $installDirectory 'Uninstall.exe'
        foreach ($installedFile in @($installedExecutable, $uninstaller, $startMenuShortcut)) {
            if (-not (Test-Path -LiteralPath $installedFile)) {
                throw "Installer did not create the expected file: $installedFile"
            }
        }
        Test-ApplicationWindow -Executable $installedExecutable

        Test-OperationRefusedWhileAppRuns `
            -ApplicationExecutable $installedExecutable `
            -OperationExecutable $installerPath `
            -OperationArguments @('/S') `
            -RequireNonZeroExit
        if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
            throw 'Setup altered the installed application despite the running-app safety lock.'
        }

        # A second silent install exercises the same-version upgrade/repair path.
        Invoke-CheckedProcess -FilePath $installerPath -ArgumentList @('/S')
        Test-ApplicationWindow -Executable $installedExecutable

        Test-OperationRefusedWhileAppRuns `
            -ApplicationExecutable $installedExecutable `
            -OperationExecutable $uninstaller `
            -OperationArguments @('/S')
        if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf) -or
            -not (Test-Path -LiteralPath $uninstallRegistryPath)) {
            throw 'Uninstall changed installation state despite the running-app safety lock.'
        }

        Invoke-CheckedProcess -FilePath $uninstaller -ArgumentList @('/S')
        $removedPaths = @(
                $installedExecutable,
                $uninstaller,
                $startMenuShortcut,
                $uninstallRegistryPath,
                $appRegistryPath)
        if (-not $hadPreexistingSettings) {
            $removedPaths += $settingsDirectory
        }
        foreach ($removedPath in $removedPaths) {
            if (Test-Path -LiteralPath $removedPath) {
                throw "Uninstall left an app-owned path behind: $removedPath"
            }
        }
    }

    Write-Host 'Portable launch, checksums, SBOM, and installer lifecycle checks passed.'
}
finally {
    if ($hadPreexistingSettings -and (Test-Path -LiteralPath $settingsBackup -PathType Container)) {
        [void] (New-Item -ItemType Directory -Path $settingsDirectory -Force)
        Copy-Item -Path (Join-Path $settingsBackup '*') -Destination $settingsDirectory -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $settingsDirectory -PathType Container) {
        Remove-Item -LiteralPath (Join-Path $settingsDirectory 'settings.json') -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $settingsDirectory 'settings.json.tmp') -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $settingsDirectory -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $smokeRoot) {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
}
