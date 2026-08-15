# Contributing to LocalPhotoPDF

Thank you for helping improve LocalPhotoPDF. Keep changes focused, explain the user benefit, and never include private photos or machine-specific information in a commit or issue.

## Development setup

You need:

- Windows 11 x64;
- PowerShell 7;
- the .NET SDK selected by [`global.json`](global.json); and
- NSIS 3.12 or later only when building the installer.

Clone your fork, then restore, build, and test with locked dependencies:

```powershell
dotnet restore .\LocalPhotoPDF.sln --locked-mode --runtime win-x64
dotnet build .\LocalPhotoPDF.sln --configuration Release --no-restore
dotnet test .\LocalPhotoPDF.sln --configuration Release --no-build --no-restore
```

Run the app during development with:

```powershell
dotnet run --project .\src\LocalPhotoPDF\LocalPhotoPDF.csproj
```

## Dependency changes

Package lock files are committed. When intentionally changing a NuGet dependency, run a normal restore to update the relevant `packages.lock.json`, inspect that diff, and include it in the same pull request. All other builds should use `--locked-mode`.

Do not add a dependency when the .NET or Windows platform already provides a clear, maintained solution. Document the licence of any accepted dependency in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Pull requests

- Add or update tests for behaviour changes.
- Keep the app per-user, and keep it offline apart from the update check, unless a proposal has been discussed first.
- Preserve cancellation, input limits, metadata removal, and atomic output behaviour.
- Update [USER-GUIDE.md](USER-GUIDE.md) in the same change when visible labels or behaviour change.
- Run the build and tests before opening the pull request.
- Do not commit generated `artifacts/`, personal images, PDFs, secrets, or private paths.

For a release-package check, run:

```powershell
pwsh .\scripts\Build-Release.ps1 -Version 1.0.0
```

The release check verifies that the pinned SDK and bundled .NET runtime packs are on the current servicing level. A scheduled GitHub Actions job repeats this check weekly so a security servicing update is visible even when application dependencies have not changed.

## If the build fails and you have not changed anything

Two causes account for almost all of these.

**"The SDK could not be resolved" or similar.** `global.json` pins the SDK feature band to
**10.0.303** with `rollForward: latestPatch`, so any `10.0.3xx` SDK is fine and any other band is
not. Run `dotnet --list-sdks` to see what you have.

**A NuGet audit error (`NU1901`–`NU1904`) on a dependency you did not touch.** Warnings are errors
in this project and NuGet auditing is on, so a newly disclosed advisory against an existing
dependency turns into a hard build failure, for you and for CI, without anyone changing a line of
code. That strictness is deliberate: it means a vulnerable dependency cannot be ignored.

To keep working locally while it is sorted out:

```powershell
dotnet build .\LocalPhotoPDF.sln -c Release -p:WarningsNotAsErrors=NU1901%3BNU1902%3BNU1903%3BNU1904
```

The `%3B` really is necessary. It is an escaped semicolon. Writing the list with plain semicolons
fails with `MSB1006: Property is not valid`, whether or not you quote it, because the shell splits
the argument before MSBuild ever sees it.

**Please open an issue rather than only working around it.** The real fix is bumping the
dependency, and the workaround must not be committed.

## Cutting a release

```powershell
pwsh .\scripts\Build-Release.ps1 -Version <x.y.z>
pwsh .\scripts\Test-ReleaseArtifacts.ps1 -Version <x.y.z>
```

`Build-Release.ps1` clears every `bin`/`obj` first, on purpose: `dotnet test` performs a full
solution build, and without the clean the publish step would reuse those assemblies and the
publish-time settings would never apply to them.

It then scans the published executable and **fails the build** if it finds the build account's
username or a user-profile path. Builds are deterministic and source paths are remapped, so a
release should never carry the identity of the machine that produced it. Checksums and provenance
attestation do not help here, because they will faithfully attest a binary that leaks.

The release also fails when the pinned SDK or the bundled runtime packs fall behind their current
security servicing level. If that stops you, bump `global.json` rather than skipping the check.

By contributing, you agree that your contribution is licensed under the project's [MIT License](LICENSE).

Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).
