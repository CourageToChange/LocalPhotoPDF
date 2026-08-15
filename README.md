# LocalPhotoPDF

LocalPhotoPDF is a free, open-source Windows app that turns photos, in the order you put them in, into one PDF. Your photos stay on your PC. There is no account, no adverts and no telemetry, and the only thing that ever uses the network is the update button, when you press it.

I scan a lot of documents and keep them as PDFs, ready to upload whenever something asks for one. I didn't want to put them through a website every time, or install something that wanted an account first, so I made this instead. It opens, it does the one job, and nothing leaves the machine.

> [!NOTE]
> The first public builds are unsigned. Windows SmartScreen may therefore show an "unrecognized app" warning. Download releases only from this project's GitHub Releases page and compare their SHA-256 checksum with `SHA256SUMS.txt` before running them. Code signing may be added in a future release.

## What you can do

- Add photos with a file picker or drag and drop.
- Review thumbnails and put pages in the order you want.
- Rotate, remove, or clear photos before conversion.
- Create A4, US Letter, or photo-matched pages without cropping.
- Choose the page margin and a High, Balanced, or Small output preset.
- Cancel a conversion and open the finished PDF or its folder.

JPEG, PNG, BMP, GIF, TIFF, and JPEG XR use Windows' built-in image decoders. Formats such as HEIC, WebP, AVIF, and camera RAW may also work when a compatible Windows codec is installed. For animated or multi-page images, LocalPhotoPDF uses the first frame.

Read the [User guide](USER-GUIDE.md) for complete, plain-English instructions and troubleshooting.

## Install or run

LocalPhotoPDF supports 64-bit Windows 11.

- **Installer:** download `LocalPhotoPDF-Setup-<version>-win-x64.exe` from GitHub Releases and run it. Installation is only for your Windows account and does not require administrator access. The optional desktop shortcut is off by default.
- **Portable:** download `LocalPhotoPDF-<version>-win-x64.zip`, extract it to a folder you control, and run `LocalPhotoPDF.exe`.

No installer or app component starts with Windows, adds file associations, runs a background service, or checks for updates on its own.

## Updating

There is a **Check for updates** button along the bottom of the window. It does nothing until you press it: there is no timer, no check at startup and no background poll, so the app makes no network request unless you ask it to.

Press it and the app asks GitHub for the newest published release. If there is one it tells you how big the download is and waits for you to say yes. What comes down is hashed and compared against the SHA-256 published with that release. A file that does not match is deleted and never run. Press the button a second time to install: the app closes so the installer can replace it, and your saved page size, margin and quality settings survive.

Update downloads are accepted only from GitHub, only over HTTPS, and a redirect to any other host is refused rather than followed.

If you would rather do it yourself, the [releases page](https://github.com/CourageToChange/LocalPhotoPDF/releases) has every version and you can install over the top of the old one. Nothing about the app changes if you never touch the button.

## Verify a release

Open PowerShell in the download folder and run:

```powershell
Get-FileHash .\LocalPhotoPDF-Setup-1.0.0-win-x64.exe -Algorithm SHA256
```

Compare the displayed hash with the matching line in `SHA256SUMS.txt`. Tagged GitHub releases also include build provenance attestations.

## Build from source

You need Windows 11 x64, PowerShell 7, and [NSIS](https://nsis.sourceforge.io/) 3.12 or later if you want to build the installer.

You also need the exact .NET SDK feature band pinned in [`global.json`](global.json), currently **10.0.303**. `rollForward` is set to `latestPatch`, so any `10.0.3xx` SDK works, but `10.0.1xx`, `10.0.2xx` or `10.0.4xx` will fail immediately with an SDK resolution error rather than a useful message. Check what you have with `dotnet --list-sdks`, and get a matching one from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download/dotnet/10.0).

Note that warnings are treated as errors here, including NuGet audit warnings. That is deliberate, but it means a newly disclosed advisory against a dependency can break the build for everyone until the dependency is bumped. If that happens to you, [`CONTRIBUTING.md`](CONTRIBUTING.md) explains the one-line local workaround, and please open an issue, because it needs fixing properly.

```powershell
dotnet restore .\LocalPhotoPDF.sln --locked-mode --runtime win-x64
dotnet build .\LocalPhotoPDF.sln --configuration Release --no-restore
dotnet test .\LocalPhotoPDF.sln --configuration Release --no-build --no-restore
```

To create the portable ZIP, installer, SBOM, and checksums:

```powershell
pwsh .\scripts\Build-Release.ps1 -Version 1.0.0
```

The release files are written to `artifacts/`. By default, the release build also fails when the pinned SDK or bundled runtime packs are behind their current security servicing level. The script accepts `-SkipInstaller` when you only need the portable package; `-SkipServicingCheck` is available only for an offline development build and should not be used for a release.

## Reporting bugs, requesting features, and giving feedback

All of it is welcome, including from people who do not write code.

- **Something is broken** → [open a bug report](https://github.com/CourageToChange/LocalPhotoPDF/issues/new?template=bug_report.yml). The form asks for your Windows version and what kind of photos were involved, because those two answers explain most problems.
- **You want it to do something it does not do** → [open a feature request](https://github.com/CourageToChange/LocalPhotoPDF/issues/new?template=feature_request.yml). Describing the problem you are trying to solve is more useful than describing the solution.
- **A question, an idea, or general feedback** → [start a discussion](https://github.com/CourageToChange/LocalPhotoPDF/discussions).
- **A security vulnerability** → please do **not** open a public issue. Use [private reporting](https://github.com/CourageToChange/LocalPhotoPDF/security/advisories/new) so it can be fixed before it is public. Details in [SECURITY.md](SECURITY.md).

⚠️ Issues are public. **Do not attach a photo you would not want strangers to see.** Describe the image instead (format, rough size, pixel dimensions).

Want to change something yourself? [CONTRIBUTING.md](CONTRIBUTING.md) covers building and testing. Small pull requests are easier to review than large ones, and an unfinished PR with a question attached is fine.

## Project policies

- [Changelog](CHANGELOG.md)
- [Privacy](PRIVACY.md)
- [Security policy](SECURITY.md)
- [Contributing](CONTRIBUTING.md)
- [Code of conduct](CODE_OF_CONDUCT.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

LocalPhotoPDF is available under the [MIT License](LICENSE).
