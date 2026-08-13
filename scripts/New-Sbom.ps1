[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [Parameter(Mandatory)]
    [string] $PublishDirectory,

    [string] $ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

$resolvedRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$publishedExecutable = Join-Path $resolvedPublishDirectory 'LocalPhotoPDF.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "The published application was not found: $publishedExecutable"
}

# Only source-project locks describe code distributed in the application.
# Test and build-only packages are deliberately excluded from the release SBOM.
$lockFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $resolvedRoot 'src') -Filter 'packages.lock.json' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|artifacts)[\\/]' }
)
if ($lockFiles.Count -eq 0) {
    throw 'No runtime packages.lock.json files were found under src.'
}

$packages = [ordered]@{}
foreach ($lockFile in $lockFiles) {
    $lock = Get-Content -LiteralPath $lockFile.FullName -Raw | ConvertFrom-Json
    foreach ($framework in $lock.dependencies.PSObject.Properties) {
        foreach ($dependency in $framework.Value.PSObject.Properties) {
            $resolvedProperty = $dependency.Value.PSObject.Properties['resolved']
            if ($null -eq $resolvedProperty -or [string]::IsNullOrWhiteSpace([string] $resolvedProperty.Value)) {
                continue
            }

            $resolvedVersion = [string] $resolvedProperty.Value
            $relativeLockPath = [IO.Path]::GetRelativePath($resolvedRoot, $lockFile.FullName).Replace('\', '/')
            $key = '{0}@{1}' -f $dependency.Name.ToLowerInvariant(), $resolvedVersion
            if (-not $packages.Contains($key)) {
                $contentHashProperty = $dependency.Value.PSObject.Properties['contentHash']
                $packages[$key] = [ordered]@{
                    Name        = $dependency.Name
                    Version     = $resolvedVersion
                    ContentHash = if ($null -eq $contentHashProperty) { $null } else { [string] $contentHashProperty.Value }
                    LockFiles   = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
                    Directness  = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
                }
            }

            [void] $packages[$key].LockFiles.Add($relativeLockPath)
            $typeProperty = $dependency.Value.PSObject.Properties['type']
            if ($null -ne $typeProperty -and $typeProperty.Value) {
                [void] $packages[$key].Directness.Add([string] $typeProperty.Value)
            }
        }
    }
}

$packageComponents = foreach ($package in ($packages.Values | Sort-Object Name, Version)) {
    $escapedName = [Uri]::EscapeDataString($package.Name)
    $escapedVersion = [Uri]::EscapeDataString($package.Version)
    $purl = "pkg:nuget/$escapedName@$escapedVersion"
    $component = [ordered]@{
        type       = 'library'
        'bom-ref'  = $purl
        name       = $package.Name
        version    = $package.Version
        scope      = 'required'
        purl       = $purl
        properties = @(
            [ordered]@{
                name  = 'localphotopdf:dependency-type'
                value = (($package.Directness | ForEach-Object { $_ }) -join ', ')
            },
            [ordered]@{
                name  = 'localphotopdf:source-lock-files'
                value = (($package.LockFiles | ForEach-Object { $_ }) -join ', ')
            }
        )
    }

    if (-not [string]::IsNullOrWhiteSpace($package.ContentHash)) {
        $component['hashes'] = @(
            [ordered]@{
                alg     = 'SHA-512'
                content = [Convert]::ToHexString([Convert]::FromBase64String($package.ContentHash)).ToLowerInvariant()
            }
        )
    }

    $component
}

# Framework references are not NuGet PackageReferences, so they do not appear in
# packages.lock.json. Read the exact self-contained runtime pack versions selected
# by restore and record the two framework packs that are actually bundled.
$assetsPath = Join-Path $resolvedRoot 'src\LocalPhotoPDF\obj\project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
    throw "Restore assets were not found: $assetsPath"
}

$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$requiredRuntimePacks = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
[void] $requiredRuntimePacks.Add('Microsoft.NETCore.App.Runtime.win-x64')
[void] $requiredRuntimePacks.Add('Microsoft.WindowsDesktop.App.Runtime.win-x64')

$runtimePackVersions = [ordered]@{}
foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
    foreach ($download in @($framework.Value.downloadDependencies)) {
        if ($null -ne $download -and $requiredRuntimePacks.Contains([string] $download.name)) {
            $runtimePackVersions[[string] $download.name] = ConvertFrom-NuGetExactRange ([string] $download.version)
        }
    }
}

if ($runtimePackVersions.Count -ne $requiredRuntimePacks.Count) {
    throw 'The exact .NET and Windows Desktop runtime pack versions could not be determined from restore assets.'
}

$runtimeComponents = foreach ($runtimePack in $runtimePackVersions.GetEnumerator() | Sort-Object Name) {
    $purl = "pkg:nuget/$([Uri]::EscapeDataString($runtimePack.Key))@$([Uri]::EscapeDataString($runtimePack.Value))"
    [ordered]@{
        type       = 'framework'
        'bom-ref'  = $purl
        name       = $runtimePack.Key
        version    = $runtimePack.Value
        scope      = 'required'
        purl       = $purl
        licenses   = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
        properties = @(
            [ordered]@{
                name  = 'localphotopdf:distribution'
                value = 'bundled in the self-contained win-x64 executable'
            }
        )
    }
}

$publishedFileComponents = foreach ($publishedFile in Get-ChildItem -LiteralPath $resolvedPublishDirectory -File) {
    if ($publishedFile.Name -eq 'LocalPhotoPDF.exe') {
        continue
    }

    $fileReference = "urn:localphotopdf:published-file:$([Uri]::EscapeDataString($publishedFile.Name))"
    [ordered]@{
        type       = 'file'
        'bom-ref'  = $fileReference
        name       = $publishedFile.Name
        scope      = 'required'
        hashes     = @(
            [ordered]@{
                alg     = 'SHA-256'
                content = (Get-FileHash -LiteralPath $publishedFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        )
        properties = @(
            [ordered]@{
                name  = 'localphotopdf:size-bytes'
                value = [string] $publishedFile.Length
            }
        )
    }
}

$applicationReference = "pkg:generic/LocalPhotoPDF@$Version"
$applicationComponent = [ordered]@{
    type      = 'application'
    'bom-ref' = $applicationReference
    name      = 'LocalPhotoPDF'
    version   = $Version
    hashes    = @(
        [ordered]@{
            alg     = 'SHA-256'
            content = (Get-FileHash -LiteralPath $publishedExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    )
    licenses  = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
    properties = @(
        [ordered]@{
            name  = 'localphotopdf:published-file'
            value = 'LocalPhotoPDF.exe'
        },
        [ordered]@{
            name  = 'localphotopdf:size-bytes'
            value = [string] (Get-Item -LiteralPath $publishedExecutable).Length
        },
        [ordered]@{
            name  = 'localphotopdf:sbom-scope'
            value = 'distributed runtime; test and build-only packages excluded'
        }
    )
}

$components = @($packageComponents) + @($runtimeComponents) + @($publishedFileComponents)
$dependencyReferences = @($components | ForEach-Object { $_.'bom-ref' } | Sort-Object)
$timestamp = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$bom = [ordered]@{
    '$schema'    = 'https://cyclonedx.org/schema/bom-1.6.schema.json'
    bomFormat    = 'CycloneDX'
    specVersion  = '1.6'
    serialNumber = "urn:uuid:$([Guid]::NewGuid())"
    version      = 1
    metadata     = [ordered]@{
        timestamp = $timestamp
        component = $applicationComponent
    }
    components   = $components
    dependencies = @(
        [ordered]@{
            ref       = $applicationReference
            dependsOn = $dependencyReferences
        }
    )
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
[void] (New-Item -ItemType Directory -Path $outputDirectory -Force)
$json = $bom | ConvertTo-Json -Depth 14
[IO.File]::WriteAllText($resolvedOutput, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

Write-Host "Wrote runtime-scoped CycloneDX SBOM: $resolvedOutput"
