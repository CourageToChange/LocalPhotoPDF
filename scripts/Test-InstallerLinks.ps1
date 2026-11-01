[CmdletBinding()]
param(
    # The script under test. Pass an older copy to prove these checks can fail.
    [string] $Script = (Join-Path $PSScriptRoot '..\installer\LocalPhotoPDF.nsi'),

    # Keeps the working folder for inspection instead of removing it.
    [switch] $KeepWorkingFolder
)

# Builds real installers from the real script and runs them silently against folders full of
# junctions. Every path, registry key and shortcut is redirected into a throwaway folder first,
# so this is safe on a PC that has LocalPhotoPDF installed.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$makeNsis = 'C:\Program Files (x86)\NSIS\makensis.exe'
if (-not (Test-Path -LiteralPath $makeNsis)) {
    $makeNsis = (Get-Command 'makensis.exe').Source
}

# The awkward name is on purpose: a Windows account can be called O'Brien, and every one of these
# paths reaches a quoted command line.
$root = Join-Path ([IO.Path]::GetTempPath()) "lpp links O'Brien & co $PID"
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalPhotoPDFLinkTest'
$appKey = 'HKCU:\Software\LocalPhotoPDFLinkTest'
$results = [Collections.Generic.List[object]]::new()

function New-Redirected {
    param([string] $Output, [string] $ElevatedAs = '2')

    $text = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Script))
    $redirect = Join-Path $root 'profile'
    foreach ($name in '$LOCALAPPDATA', '$SMPROGRAMS', '$DESKTOP') {
        $text = $text.Replace($name, $redirect)
    }
    $text = $text.Replace('"Software\LocalPhotoPDF"', '"Software\LocalPhotoPDFLinkTest"')
    $text = $text.Replace('Uninstall\LocalPhotoPDF"', 'Uninstall\LocalPhotoPDFLinkTest"')
    $text = $text.Replace('LocalPhotoPDF.AppMutex.v1', 'LocalPhotoPDF.AppMutex.LinkTest')
    $text = $text.Replace('StrCmp $3 2 0 elevation_checked', "StrCmp `$3 $ElevatedAs 0 elevation_checked")

    if ($text -match '\$LOCALAPPDATA|\$SMPROGRAMS|\$DESKTOP') {
        throw 'A path in the script escaped the redirect. Refusing to build it.'
    }
    $copy = "$Output.nsi"
    [IO.File]::WriteAllText($copy, $text, [Text.UTF8Encoding]::new($true))
    & $makeNsis /V1 "/DAPP_VERSION=9.9.9" "/DSOURCE_DIR=$(Join-Path $root 'payload')" "/DOUTPUT_FILE=$Output" $copy | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makensis failed with exit code $LASTEXITCODE" }
}

function Remove-Link {
    param([string] $Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if (-not $item) { return }
    if ($item.PSIsContainer) { cmd.exe /d /c rmdir /s /q "$Path" | Out-Null }
    else {
        # Windows briefly holds a shortcut it has just seen written.
        foreach ($attempt in 1..20) {
            try { Remove-Item -LiteralPath $Path -Force; return }
            catch { if ($attempt -eq 20) { throw } }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Reset-World {
    Get-ChildItem -LiteralPath (Join-Path $root 'profile') -Force -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Link $_.FullName }
    Remove-Link (Join-Path $root 'canary')
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'profile\Programs'), (Join-Path $root 'canary') | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'canary\keep.txt') -Value 'must survive'
    Remove-Item -LiteralPath $uninstallKey, $appKey -Recurse -Force -ErrorAction SilentlyContinue
}

function New-OldInstall {
    New-Item -ItemType Directory -Force -Path "$install\licenses" | Out-Null
    Set-Content -LiteralPath "$install\LocalPhotoPDF.exe" -Value 'old'
    Set-Content -LiteralPath "$install\old-only.txt" -Value 'old'
}

function New-Junction {
    param([string] $Path, [string] $Target = (Join-Path $root 'canary'))
    New-Item -ItemType Junction -Path $Path -Target $Target | Out-Null
}

function Invoke-Setup {
    param([string] $Exe = $setup)
    $process = Start-Process -FilePath $Exe -ArgumentList '/S' -Wait -PassThru
    return $process.ExitCode
}

function Add-Result {
    param([string] $Name, [string[]] $Failures)
    $results.Add([pscustomobject]@{ Scenario = $Name; Result = if ($Failures) { 'FAIL' } else { 'pass' }; Detail = $Failures -join '; ' })
}

function Test-Canary { Test-Path -LiteralPath (Join-Path $root 'canary\keep.txt') }

function Test-NewInstall {
    # A run that changed nothing must not count, so the new version has to really be in place.
    (Test-Path -LiteralPath "$install\new-marker.txt") -and -not (Test-Path -LiteralPath "$install\old-only.txt")
}

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'payload\licenses'), (Join-Path $root 'profile') | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'payload\LocalPhotoPDF.exe') -Value 'new'
    Set-Content -LiteralPath (Join-Path $root 'payload\new-marker.txt') -Value 'new'
    Set-Content -LiteralPath (Join-Path $root 'payload\licenses\PDFsharp-LICENSE.txt') -Value 'new'

    $install = Join-Path $root 'profile\Programs\LocalPhotoPDF'
    $staging = "$install.staging"
    $previous = "$install.previous"
    $setup = Join-Path $root 'setup.exe'
    New-Redirected -Output $setup

    $linkCases = [ordered]@{
        'previous is a junction'           = { New-OldInstall; New-Junction $previous }
        'staging is a junction'            = { New-OldInstall; New-Junction $staging }
        'junction inside the old install'  = { New-OldInstall; New-Junction "$install\nested" }
        'install folder is a junction'     = { New-Junction $install }
        'install missing, previous a link' = { New-Junction $previous }
    }
    foreach ($case in $linkCases.GetEnumerator()) {
        Reset-World
        & $case.Value
        $failures = @()
        if (-not (Test-Canary)) { throw "The canary was missing before '$($case.Key)' ran." }
        $code = Invoke-Setup
        if (-not (Test-Canary)) { $failures += 'CANARY DELETED' }
        if ($code -ne 0) { $failures += "exit $code" }
        if (-not (Test-Path -LiteralPath "$install\new-marker.txt")) { $failures += 'new version not installed' }
        if (Test-Path -LiteralPath $previous) { $failures += 'previous left behind' }
        if (Test-Path -LiteralPath $staging) { $failures += 'staging left behind' }
        Add-Result $case.Key $failures
    }

    Reset-World
    New-OldInstall
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'gone') | Out-Null
    New-Junction $previous (Join-Path $root 'gone')
    Remove-Item -LiteralPath (Join-Path $root 'gone') -Force
    $failures = @()
    $code = Invoke-Setup
    if ($code -ne 0) { $failures += "exit $code" }
    if (-not (Test-NewInstall)) { $failures += 'new version not installed' }
    Add-Result 'previous is a dangling junction' $failures

    Reset-World
    New-OldInstall
    New-Item -ItemType Directory -Force -Path $staging, $previous | Out-Null
    Set-Content -LiteralPath "$staging\stale.txt" -Value 'stale'
    Set-Content -LiteralPath "$previous\stale.txt" -Value 'stale'
    $failures = @()
    $code = Invoke-Setup
    if ($code -ne 0) { $failures += "exit $code" }
    if (-not (Test-NewInstall)) { $failures += 'new version not installed' }
    if ((Test-Path -LiteralPath $staging) -or (Test-Path -LiteralPath $previous)) { $failures += 'leftovers not cleared' }
    Add-Result 'ordinary upgrade over real leftovers' $failures

    Reset-World
    Invoke-Setup | Out-Null
    New-Junction $staging
    New-Junction $previous
    $failures = @()
    if (-not (Test-Path -LiteralPath "$install\Uninstall.exe")) { throw 'Setup did not install an uninstaller to test.' }
    $null = Start-Process -FilePath "$install\Uninstall.exe" -ArgumentList '/S' -PassThru
    # The uninstaller hands over to a temporary copy of itself and exits at once.
    foreach ($attempt in 1..60) {
        if (-not (Test-Path -LiteralPath $appKey)) { break }
        Start-Sleep -Milliseconds 500
    }
    Start-Sleep -Milliseconds 500
    if (-not (Test-Canary)) { $failures += 'CANARY DELETED' }
    if (Test-Path -LiteralPath $appKey) { $failures += 'uninstall did not finish' }
    if ((Test-Path -LiteralPath $staging) -or (Test-Path -LiteralPath $previous)) { $failures += 'links left behind' }
    Add-Result 'uninstall with both links planted' $failures

    # Nothing here can raise a UAC prompt, so the refusal is proved by a build that treats any token
    # as the elevated one. Only that comparison differs from the real installer.
    $text = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Script))
    if ($text.Contains('StrCmp $3 2 0 elevation_checked')) {
        $refuser = Join-Path $root 'refuser.exe'
        New-Redirected -Output $refuser -ElevatedAs '$3'
        Reset-World
        New-OldInstall
        $failures = @()
        $code = Invoke-Setup -Exe $refuser
        if ($code -ne 1) { $failures += "exit $code, expected 1" }
        if (-not (Test-Path -LiteralPath "$install\old-only.txt")) { $failures += 'install changed' }
        if (Test-Path -LiteralPath $staging) { $failures += 'staging created' }
        Add-Result 'elevated run is refused (this token standing in)' $failures
    }
    else {
        Add-Result 'elevated run is refused (this token standing in)' @('no refusal in this script')
    }
}
finally {
    Remove-Item -LiteralPath $uninstallKey, $appKey -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $KeepWorkingFolder -and (Test-Path -LiteralPath $root)) {
        Reset-World
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$results | Format-Table -AutoSize -Wrap | Out-String -Width 120 | Write-Host
$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host "$($results.Count) scenarios, $failed failed"
if ($results.Count -eq 0 -or $failed -gt 0) { exit 1 }
