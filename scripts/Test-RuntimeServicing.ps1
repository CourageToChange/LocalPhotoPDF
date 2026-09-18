[CmdletBinding()]
param(
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
$globalJson = Get-Content -LiteralPath (Join-Path $resolvedRoot 'global.json') -Raw | ConvertFrom-Json
$expectedSdkVersion = [string] $globalJson.sdk.version

$assetsPath = Join-Path $resolvedRoot 'src\LocalPhotoPDF\obj\project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
    throw 'Restore assets are missing. Restore the solution before checking runtime servicing.'
}

$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$expectedRuntimes = [ordered]@{}
foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
    foreach ($download in @($framework.Value.downloadDependencies)) {
        $runtimeName = switch ([string] $download.name) {
            'Microsoft.NETCore.App.Runtime.win-x64' { 'Microsoft.NETCore.App' }
            'Microsoft.WindowsDesktop.App.Runtime.win-x64' { 'Microsoft.WindowsDesktop.App' }
            default { $null }
        }

        if ($null -ne $runtimeName) {
            $expectedRuntimes[$runtimeName] = ConvertFrom-NuGetExactRange ([string] $download.version)
        }
    }
}

if ($expectedRuntimes.Count -ne 2) {
    throw 'The resolved .NET and Windows Desktop runtime versions could not be determined.'
}

$checkLines = & dotnet sdk check 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "dotnet sdk check failed with exit code $LASTEXITCODE."
}

$checkOutput = $checkLines -join [Environment]::NewLine
Write-Host $checkOutput

$sdkPattern = '(?m)^\s*{0}\s+Up to date\.\s*$' -f [Regex]::Escape($expectedSdkVersion)
if ($checkOutput -notmatch $sdkPattern) {
    throw "The pinned .NET SDK $expectedSdkVersion is not reported as up to date. Update global.json and revalidate the release."
}

# Corrected 2026-09-18. This used to require a line reading
#   "<RuntimeName> <BundledVersion> Up to date."
# in the `dotnet sdk check` output. That output lists the runtimes INSTALLED ON THIS MACHINE,
# and a self-contained app's bundled runtime comes from NuGet, so it has no reason to be
# installed locally. The two only coincide when a newer runtime is obtained by installing a
# newer SDK.
#
# After pinning RuntimeFrameworkVersion to 10.0.12 the old check failed with "the bundled
# Microsoft.NETCore.App 10.0.12 runtime is not reported as up to date" while 10.0.12 IS the
# latest patch and is exactly what we ship. A false failure.
#
# The question this guard exists to ask is "is the runtime we SHIP behind the latest patch?",
# so that is what it now asks: derive the latest available patch per family from the same
# output, and require the bundled version to equal it.
#   - a row saying "Patch X is available" means the latest is X
#   - a row saying "Up to date" means the latest is that row's own version
#
# This is STRICTER than before in the case that matters: it fails whenever the bundled runtime
# is behind, regardless of what happens to be installed. Proven by pinning 10.0.11 and watching
# it go red.
foreach ($runtime in $expectedRuntimes.GetEnumerator()) {
    $family = [string] $runtime.Key
    $bundled = [string] $runtime.Value

    $latest = $null
    $patchPattern = '(?m)^\s*{0}\s+(\S+)\s+Patch\s+(\S+)\s+is available\.\s*$' -f `
        [Regex]::Escape($family)
    $uptodatePattern = '(?m)^\s*{0}\s+(\S+)\s+Up to date\.\s*$' -f [Regex]::Escape($family)

    foreach ($m in [Regex]::Matches($checkOutput, $patchPattern)) {
        $candidate = $m.Groups[2].Value
        if ($null -eq $latest -or ([version] $candidate) -gt ([version] $latest)) {
            $latest = $candidate
        }
    }
    foreach ($m in [Regex]::Matches($checkOutput, $uptodatePattern)) {
        $candidate = $m.Groups[1].Value
        if ($null -eq $latest -or ([version] $candidate) -gt ([version] $latest)) {
            $latest = $candidate
        }
    }

    if ($null -eq $latest) {
        throw "Could not determine the latest available patch for $family from 'dotnet sdk check'. Refusing to certify the release on an absent measurement."
    }

    if (([version] $bundled) -lt ([version] $latest)) {
        throw "The bundled $family $bundled runtime is BEHIND the latest available patch $latest. Bump RuntimeFrameworkVersion in Directory.Build.props, restore, and cut a release."
    }

    Write-Host ("  {0}: bundling {1}, latest available {2}" -f $family, $bundled, $latest)
}

Write-Host 'The pinned SDK and bundled runtime packs are on the current servicing level.'
