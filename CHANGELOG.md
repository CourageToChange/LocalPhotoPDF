# Changelog

All notable changes to LocalPhotoPDF are recorded here.

This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html), and the format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.0] — 2026-08-12

First public release.

### Added

- Turn photos, in the order you put them in, into a single PDF - entirely on your own PC.
- Drag and drop, or add photos through the file picker. Reorder by dragging, or with
  <kbd>Alt</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd>.
- Page size, margin and quality settings. An A4 or Letter page turns sideways to suit a landscape
  photo, and nothing is ever cropped to fit.
- The same picture saved under two different filenames is stored once in the PDF, not twice.
- EXIF orientation is honoured, so photos are not sideways.
- Cancel a long import or a long conversion without losing the rest of your list.
- Import safety limits: 500 photos, 250 MiB per file, 200 megapixels per image. Anything rejected
  is reported, never silently dropped.
- Portable ZIP and a per-user installer that needs no administrator rights.
- A CycloneDX SBOM and SHA-256 checksums published with every release.

### Security and privacy

- **No network code exists in the application at all.** No account, no adverts, no telemetry, no
  update check. Photos never leave the machine. See [PRIVACY.md](PRIVACY.md).
- Releases are **not code-signed**, so Windows SmartScreen shows a warning on first run. Verify the
  SHA-256 checksum against `SHA256SUMS.txt` before running anything you download. See
  [SECURITY.md](SECURITY.md).

### Notes on how this was built

I built the first version with AI coding agents, then went through the result myself before
releasing it. That turned up seven defects the tests had not caught - one that could leave the
window permanently unresponsive, with Task Manager the only way out; a "Show in Folder" button
that opened the wrong folder and reported success either way; and a leak that grew every time the
imaging work landed on a new thread. The test suite went from 23 to 28 in the process.

An eighth turned up later, and it is the one I would have hated to ship: the released binary
embedded the full path it was built from, so anyone who ran `strings` on a download would have
seen the account name and folder layout of the machine that produced it. No checksum or provenance
attestation catches that - both will happily attest a file that leaks. The build now refuses to
package a binary containing that path.

I would rather have that written down than not.

[1.0.0]: https://github.com/CourageToChange/LocalPhotoPDF/releases/tag/v1.0.0
