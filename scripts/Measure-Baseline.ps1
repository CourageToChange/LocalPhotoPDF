<#
    Measure-Baseline.ps1 — the optimization phase's baseline for LocalPhotoPDF.

    Two metrics, because they are the two a person actually feels:

      1. STARTUP   — launching the exe to a window you can use.
      2. PDF BUILD — a realistic batch of photos becoming a PDF.

    It measures the RELEASE build, because a Debug build is not what anyone runs, and it
    prints every run rather than an average, so one slow outlier cannot hide inside a mean.
    Nothing here changes the app.

    Usage:
      pwsh .\scripts\Measure-Baseline.ps1
      pwsh .\scripts\Measure-Baseline.ps1 -Runs 5 -Photos 60
      pwsh .\scripts\Measure-Baseline.ps1 -Json baseline.json

    Build first:  dotnet build LocalPhotoPDF.sln -c Release

    ⚠️ Deliberately written without helper functions that return objects. The first version
    used them and returned $null while printing no error, because a PowerShell function
    returns *everything* uncaptured and `Set-StrictMode` then turned the null property
    access into "The property 'Times' cannot be found" — an error about the wrong thing
    entirely. Straight-line code cannot fail that way.
#>
[CmdletBinding()]
param(
    [int] $Runs = 4,
    [int] $Photos = 30,
    [string] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appDir = Join-Path $root 'src\LocalPhotoPDF\bin\Release\net10.0-windows\win-x64'
$exe    = Join-Path $appDir 'LocalPhotoPDF.exe'

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "  No Release build at $appDir"
    Write-Host "  Run: dotnet build LocalPhotoPDF.sln -c Release"
    exit 1
}

Write-Host ''
Write-Host '  LocalPhotoPDF baseline'
Write-Host "  commit $(git -C $root rev-parse --short HEAD)   taken $(Get-Date -Format s)"

# ---------------------------------------------------------------------------
# 1. Startup
# ---------------------------------------------------------------------------
# Timed to MainWindowHandle, not to Process.Start returning. The handle appearing is the
# first moment there is something to look at, which is the thing worth optimising.
Write-Host ''
Write-Host "  1. Startup: launch to usable window ($Runs runs)"
Write-Host '  --------------------------------------------'

$startupTimes = @()
$startupPeakMB = 0
foreach ($i in 1..$Runs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $exe -PassThru
    try {
        while (-not $p.HasExited -and $p.MainWindowHandle -eq 0 -and $sw.Elapsed.TotalSeconds -lt 30) {
            Start-Sleep -Milliseconds 5
            $p.Refresh()
        }
        $sw.Stop()
        if ($p.MainWindowHandle -eq 0) { Write-Host "    run ${i}: no window within 30s"; continue }
        $startupTimes += [math]::Round($sw.Elapsed.TotalMilliseconds, 0)
        if ($startupPeakMB -eq 0) { $startupPeakMB = [math]::Round($p.PeakWorkingSet64 / 1MB, 1) }
    }
    finally {
        if (-not $p.HasExited) { [void]$p.CloseMainWindow(); Start-Sleep -Milliseconds 700 }
        if (-not $p.HasExited) { $p.Kill() }
    }
    Start-Sleep -Milliseconds 400
}

$startupSorted = @($startupTimes | Sort-Object)
if ($startupSorted.Count -gt 0) {
    $startupMedian = $startupSorted[[math]::Floor($startupSorted.Count / 2)]
    Write-Host "    runs: $($startupTimes -join ', ') ms"
    Write-Host "    min $($startupSorted[0])  median $startupMedian  max $($startupSorted[-1])"
    Write-Host "    peak working set at window: $startupPeakMB MB"
} else {
    $startupMedian = 0
    Write-Host '    no successful startup runs'
}

# ---------------------------------------------------------------------------
# 2. PDF build
# ---------------------------------------------------------------------------
# Driven through Core, where the work happens and where the tests are.
#
# Loaded from the APP's output folder, not the library's own: only that folder has PDFsharp
# beside it, and Core alone fails at the first GenerateAsync with "Could not load file or
# assembly 'PdfSharp-wpf'". Dependencies are pre-loaded rather than hooked through
# AssemblyResolve, because a resolver that calls LoadFrom re-enters itself and kills the
# process with a stack overflow.
Write-Host ''
Write-Host "  2. PDF build: $Photos photos -> one PDF ($Runs runs)"
Write-Host '  -------------------------------------------'

foreach ($dep in 'PdfSharp-wpf', 'PdfSharp.Cryptography', 'Microsoft.Extensions.Logging.Abstractions') {
    $dll = Join-Path $appDir "$dep.dll"
    if (Test-Path -LiteralPath $dll) { [void][Reflection.Assembly]::LoadFrom($dll) }
}
[void][Reflection.Assembly]::LoadFrom((Join-Path $appDir 'LocalPhotoPDF.Core.dll'))
Add-Type -AssemblyName System.Drawing

$work = Join-Path ([IO.Path]::GetTempPath()) ('lpp-baseline-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work -Force | Out-Null

$pdfTimes = @()
$pdfMedian = 0
$outMB = 0
$srcMB = 0
try {
    # 3000x2000 is roughly a phone photo. The ellipses matter: a flat colour compresses to
    # almost nothing and the measurement stops resembling a photograph.
    foreach ($i in 1..$Photos) {
        $bmp = New-Object System.Drawing.Bitmap 3000, 2000
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb((($i * 7) % 255), (($i * 13) % 255), (($i * 29) % 255)))
        for ($k = 0; $k -lt 300; $k++) {
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb((($k * 31) % 255), (($k * 17) % 255), (($k * 3) % 255)))
            $g.FillEllipse($brush, (($k * 61) % 2900), (($k * 37) % 1900), 90, 90)
            $brush.Dispose()
        }
        $g.Dispose()
        $bmp.Save((Join-Path $work "p$i.jpg"), [System.Drawing.Imaging.ImageFormat]::Jpeg)
        $bmp.Dispose()
    }
    $srcMB = [math]::Round(((Get-ChildItem $work -Filter *.jpg | Measure-Object Length -Sum).Sum / 1MB), 1)
    Write-Host "    generated $Photos photos, $srcMB MB of source"

    $sources = [System.Collections.Generic.List[LocalPhotoPDF.Core.PhotoSource]]::new()
    foreach ($path in (Get-ChildItem $work -Filter *.jpg | Sort-Object Name).FullName) {
        [void]$sources.Add([LocalPhotoPDF.Core.PhotoSource]::new($path, 0))
    }

    foreach ($r in 1..$Runs) {
        $outPath = Join-Path $work "out$r.pdf"
        [GC]::Collect(); [GC]::WaitForPendingFinalizers()
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $svc = [LocalPhotoPDF.Core.PdfGenerationService]::new()
        $req = [LocalPhotoPDF.Core.PdfBuildRequest]::new($sources, $outPath)
        $svc.GenerateAsync($req, $null, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $sw.Stop()
        $pdfTimes += [math]::Round($sw.Elapsed.TotalMilliseconds, 0)
        $outMB = [math]::Round((Get-Item $outPath).Length / 1MB, 2)
        Remove-Item $outPath -Force
    }

    $pdfSorted = @($pdfTimes | Sort-Object)
    $pdfMedian = $pdfSorted[[math]::Floor($pdfSorted.Count / 2)]
    Write-Host "    runs: $($pdfTimes -join ', ') ms"
    Write-Host "    min $($pdfSorted[0])  median $pdfMedian  max $($pdfSorted[-1])"
    Write-Host "    per photo (median): $([math]::Round($pdfMedian / $Photos, 1)) ms"
    Write-Host "    output $outMB MB from $srcMB MB of source"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Json) {
    [pscustomobject]@{
        commit        = (git -C $root rev-parse --short HEAD)
        takenAt       = (Get-Date -Format s)
        photos        = $Photos
        startupMs     = $startupTimes
        startupMedian = $startupMedian
        startupPeakMB = $startupPeakMB
        pdfMs         = $pdfTimes
        pdfMedian     = $pdfMedian
        pdfPerPhotoMs = if ($Photos -gt 0) { [math]::Round($pdfMedian / $Photos, 1) } else { 0 }
        outputMB      = $outMB
        sourceMB      = $srcMB
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Json -Encoding utf8
    Write-Host ''
    Write-Host "  saved $Json"
}
Write-Host ''
