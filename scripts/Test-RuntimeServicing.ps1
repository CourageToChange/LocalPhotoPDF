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

foreach ($runtime in $expectedRuntimes.GetEnumerator()) {
    $runtimePattern = '(?m)^\s*{0}\s+{1}\s+Up to date\.\s*$' -f (
        [Regex]::Escape([string] $runtime.Key),
        [Regex]::Escape([string] $runtime.Value))
    if ($checkOutput -notmatch $runtimePattern) {
        throw "The bundled $($runtime.Key) $($runtime.Value) runtime is not reported as up to date. Restore with a serviced SDK before releasing."
    }
}

Write-Host 'The pinned SDK and bundled runtime packs are on the current servicing level.'
