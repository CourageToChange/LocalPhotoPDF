[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.0.0',

    [ValidateSet('Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipInstaller,

    [switch] $SkipServicingCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-NativeCommand {
    param(
        [string] $Executable,
        [string[]] $CommandArguments
    )

    & $Executable @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $Executable $($CommandArguments -join ' ')"
    }
}

function Find-MakeNsis {
    $fromPath = Get-Command 'makensis.exe' -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    $candidates = @(
        'C:\Program Files (x86)\NSIS\makensis.exe',
        'C:\Program Files\NSIS\makensis.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw 'NSIS makensis.exe was not found on PATH or in a standard Program Files location. Install NSIS or use -SkipInstaller.'
}

function ConvertFrom-NuGetExactRange {
    param([Parameter(Mandatory)][string] $Value)

    if ($Value -match '^\[\s*([^,\]]+)\s*,\s*([^\]]+)\s*\]$') {
        $minimum = $Matches[1].Trim()
        $maximum = $Matches[2].Trim()
        if ($minimum -ne $maximum) {
            throw "Expected an exact NuGet version range, received: $Value"
        }

        return $minimum
    }

    if ($Value -match '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
        return $Value
    }

    throw "Could not parse exact NuGet version: $Value"
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $projectRoot 'LocalPhotoPDF.sln'
$appProject = Join-Path $projectRoot 'src\LocalPhotoPDF\LocalPhotoPDF.csproj'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$expectedArtifactPrefix = $projectRoot.TrimEnd('\') + '\'

if (-not $artifactRoot.StartsWith($expectedArtifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected artifact path: $artifactRoot"
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

$stagingRoot = Join-Path $artifactRoot 'staging'
$publishDirectory = Join-Path $stagingRoot 'publish'
$distributionName = "LocalPhotoPDF-$Version-win-x64"
$distributionDirectory = Join-Path $stagingRoot $distributionName
[void] (New-Item -ItemType Directory -Path $publishDirectory -Force)
[void] (New-Item -ItemType Directory -Path $distributionDirectory -Force)

Push-Location $projectRoot
try {
    # Clear every intermediate directory before restoring.
    #
    # 'dotnet test' below performs a full solution build. Without this, MSBuild's incremental build
    # sees LocalPhotoPDF.Core.dll as already up to date and the publish step reuses that DLL
    # verbatim - so the publish-time flags (DebugType=None and friends) never apply to it, and
    # whatever was baked in during the test build ships. That is exactly how a build path
    # containing the developer's Windows username reached the released binary.
    Write-Host 'Clearing intermediate build output (bin/obj)...'
    foreach ($stale in @('src', 'tests')) {
        $staleRoot = Join-Path $projectRoot $stale
        if (Test-Path -LiteralPath $staleRoot) {
            Get-ChildItem -LiteralPath $staleRoot -Directory -Recurse -Force |
                Where-Object { $_.Name -in @('bin', 'obj') } |
                ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }

    Write-Host 'Restoring locked dependencies...'
    Invoke-NativeCommand -Executable dotnet -CommandArguments @(
        'restore', $solutionPath, '--locked-mode', '--runtime', 'win-x64'
    )

    if (-not $SkipServicingCheck) {
        Write-Host 'Checking .NET SDK and runtime servicing status...'
        & (Join-Path $PSScriptRoot 'Test-RuntimeServicing.ps1') -ProjectRoot $projectRoot
    }

    Write-Host 'Running tests...'
    Invoke-NativeCommand -Executable dotnet -CommandArguments @(
        'test', $solutionPath,
        '--configuration', $Configuration,
        '--no-restore',
        '--logger', 'console;verbosity=normal'
    )

    Write-Host 'Publishing self-contained Windows x64 application...'
    Invoke-NativeCommand -Executable dotnet -CommandArguments @(
        'publish', $appProject,
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--no-restore',
        '--output', $publishDirectory,
        '-p:PublishSingleFile=true',
        '-p:PublishTrimmed=false',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0",
        "-p:FileVersion=$Version.0"
    )

    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'LocalPhotoPDF.exe') -PathType Leaf)) {
        throw 'The publish completed without producing LocalPhotoPDF.exe.'
    }

    # Refuse to release a binary that names the machine it was built on.
    #
    # A release build once shipped with the full build path embedded, exposing the developer's
    # Windows username and folder layout to anyone who ran `strings` on a public download.
    # Checksums and provenance attestation do NOT protect against this - they will faithfully
    # attest a leaking file. So the check happens here, and it fails the build.
    Write-Host 'Scanning the published binary for build-machine identifiers...'
    $publishedExe = Join-Path $publishDirectory 'LocalPhotoPDF.exe'
    $exeBytes = [System.IO.File]::ReadAllBytes($publishedExe)
    # Read as latin-1 so every byte maps to one char; ASCII paths survive intact.
    $exeText = [System.Text.Encoding]::GetEncoding(28591).GetString($exeBytes)
    $identifiers = @{
        'the build account username' = [Environment]::UserName
        'a user-profile path'        = 'C:\Users\'
        'a home-directory path'      = '/home/'
    }
    $leaks = @()
    foreach ($entry in $identifiers.GetEnumerator()) {
        if ($entry.Value -and $exeText.Contains($entry.Value)) { $leaks += $entry.Key }
    }
    if ($leaks.Count -gt 0) {
        throw ("REFUSING TO RELEASE: the published binary embeds " + ($leaks -join ', ') + ". " +
               'This usually means ContinuousIntegrationBuild is off, or a stale bin/obj was reused. ' +
               "Inspect with: strings '$publishedExe' | Select-String 'Users'")
    }
    Write-Host '  clean - no build-machine identifiers found.'

    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $distributionDirectory -Recurse -Force
    foreach ($documentationFile in @('LICENSE', 'README.md', 'USER-GUIDE.md', 'PRIVACY.md', 'SECURITY.md', 'THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $documentationFile) -Destination $distributionDirectory -Force
    }
    $distributionLicenses = Join-Path $distributionDirectory 'licenses'
    [void] (New-Item -ItemType Directory -Path $distributionLicenses -Force)
    Copy-Item -Path (Join-Path $projectRoot 'licenses\*') -Destination $distributionLicenses -Force

    # Copy notices from the exact runtime packs selected by this restore instead
    # of from the machine-wide SDK, which may describe a different payload.
    $assetsPath = Join-Path $projectRoot 'src\LocalPhotoPDF\obj\project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packageRoot = ($assets.packageFolders.PSObject.Properties | Select-Object -First 1).Name
    $runtimeVersions = @{}
    foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
        foreach ($download in @($framework.Value.downloadDependencies)) {
            if ($download.name -in @(
                    'Microsoft.NETCore.App.Runtime.win-x64',
                    'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
                $runtimeVersions[[string] $download.name] = ConvertFrom-NuGetExactRange ([string] $download.version)
            }
        }
    }

    $netCoreRuntimeDirectory = Join-Path $packageRoot (
        'microsoft.netcore.app.runtime.win-x64\' +
        $runtimeVersions['Microsoft.NETCore.App.Runtime.win-x64'])
    $windowsDesktopRuntimeDirectory = Join-Path $packageRoot (
        'microsoft.windowsdesktop.app.runtime.win-x64\' +
        $runtimeVersions['Microsoft.WindowsDesktop.App.Runtime.win-x64'])
    $extensionsLibrary = @(
        $assets.libraries.PSObject.Properties |
            Where-Object { $_.Name -like 'Microsoft.Extensions.Logging.Abstractions/*' }
    )
    if ($extensionsLibrary.Count -ne 1) {
        throw 'Could not identify the exact Microsoft.Extensions package notice source.'
    }
    $extensionsDirectory = Join-Path $packageRoot ([string] $extensionsLibrary[0].Value.path)
    $runtimeNotices = @(
        @{
            Source = Join-Path $netCoreRuntimeDirectory 'LICENSE.TXT'
            Name = 'dotnet-runtime-LICENSE.txt'
        },
        @{
            Source = Join-Path $netCoreRuntimeDirectory 'THIRD-PARTY-NOTICES.TXT'
            Name = 'dotnet-runtime-THIRD-PARTY-NOTICES.txt'
        },
        @{
            Source = Join-Path $windowsDesktopRuntimeDirectory 'LICENSE'
            Name = 'windowsdesktop-runtime-LICENSE.txt'
        },
        @{
            Source = Join-Path $extensionsDirectory 'LICENSE.TXT'
            Name = 'microsoft-extensions-LICENSE.txt'
        },
        @{
            Source = Join-Path $extensionsDirectory 'THIRD-PARTY-NOTICES.TXT'
            Name = 'microsoft-extensions-THIRD-PARTY-NOTICES.txt'
        }
    )
    foreach ($notice in $runtimeNotices) {
        if (-not (Test-Path -LiteralPath $notice.Source -PathType Leaf)) {
            throw "Required runtime notice was not found: $($notice.Source)"
        }

        Copy-Item -LiteralPath $notice.Source -Destination (Join-Path $distributionLicenses $notice.Name) -Force
    }

    $sbomName = "LocalPhotoPDF-$Version-sbom.cdx.json"
    $sbomPath = Join-Path $artifactRoot $sbomName
    & (Join-Path $PSScriptRoot 'New-Sbom.ps1') `
        -Version $Version `
        -OutputPath $sbomPath `
        -PublishDirectory $publishDirectory `
        -ProjectRoot $projectRoot
    Copy-Item -LiteralPath $sbomPath -Destination (Join-Path $distributionDirectory 'SBOM.cdx.json') -Force

    $portablePath = Join-Path $artifactRoot "$distributionName.zip"
    Write-Host "Creating portable package: $portablePath"
    Compress-Archive -LiteralPath $distributionDirectory -DestinationPath $portablePath -CompressionLevel Optimal

    if (-not $SkipInstaller) {
        $makeNsis = Find-MakeNsis
        $installerPath = Join-Path $artifactRoot "LocalPhotoPDF-Setup-$Version-win-x64.exe"
        Write-Host "Creating per-user installer: $installerPath"
        Invoke-NativeCommand -Executable $makeNsis -CommandArguments @(
            "/DAPP_VERSION=$Version",
            "/DSOURCE_DIR=$distributionDirectory",
            "/DOUTPUT_FILE=$installerPath",
            (Join-Path $projectRoot 'installer\LocalPhotoPDF.nsi')
        )
    }

    $checksumPath = Join-Path $artifactRoot 'SHA256SUMS.txt'
    $releaseFiles = Get-ChildItem -LiteralPath $artifactRoot -File |
        Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
        Sort-Object Name
    $checksumLines = foreach ($file in $releaseFiles) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($file.Name)"
    }
    [IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))

    Remove-Item -LiteralPath $stagingRoot -Recurse -Force

    Write-Host ''
    Write-Host 'Release artifacts:'
    Get-ChildItem -LiteralPath $artifactRoot -File | Sort-Object Name | ForEach-Object {
        Write-Host "  $($_.FullName)"
    }
}
finally {
    Pop-Location
}
