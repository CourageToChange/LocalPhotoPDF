# Changelog

All notable changes to LocalPhotoPDF are recorded here.

This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html), and the format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.1.0] — 2026-08-24

### Fixed

- **The Page size, Margin and Quality dropdowns showed nothing.** Open one and the list was
  blank, though the options were still there and could still be chosen. That is worse than a
  control that plainly does not work, because you could change a setting without being able to
  read what you were changing.
  One mistake caused this and the brightness complaint below. The app asked Windows for its
  theme, then painted every surface with a much older set of Windows colours that are fixed to
  light regardless of your theme. The two ignore each other. On a PC set to dark mode the window
  forced itself bright while the dropdown list stayed dark and took its text colour from the
  window, so the text came out black on a near black background. Measured, that is a contrast
  ratio of 1.48 to 1, where readable text needs 4.5 to 1. Dropdown text now measures 11.44 to 1.
  Every colour in the app is now checked against that requirement by a test, so this particular
  fault cannot come back quietly.

- **Screen readers now read the dropdown options properly.** They previously announced the
  internal record behind each option, so instead of hearing "A4, automatic orientation" you would
  hear something closer to `OptionChoice { Value = A4, Label = A4 ... }`. Each option now announces
  its name, and its explanation is available as help text. The dropdowns looked entirely correct on
  screen while this was happening, which is why it went unnoticed.

### Added

- **Dark mode, and it is on by default.** Light mode is one click away in Settings.
- **A Settings screen.** Choose a folder to save PDFs to, so the save dialog opens where you
  actually keep things instead of Documents every time. Switch theme, with the change previewed
  live and undone if you press Cancel. Optionally open the containing folder when an export
  finishes. Your choices are remembered between sessions, in the same local settings file the
  app already used, and nothing is sent anywhere.

## [1.0.0] — 2026-08-16

First public release.

### Added

- Turn photos, in the order you put them in, into a single PDF, entirely on your own PC.
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

- **The app never contacts the internet on its own.** No account, no adverts, no telemetry, no
  background poll, no check at startup. Photos never leave the machine. See [PRIVACY.md](PRIVACY.md).
- **Check for updates** along the bottom of the window is the only feature that uses the network,
  and only while you are pressing it. It asks GitHub for the newest published release, tells you
  what it found, and asks before downloading. GitHub sees your IP address and a user agent that
  says `LocalPhotoPDF-update-check`, the same as any browser opening a page. There is no account,
  no machine name and no installation identifier, and the app does not send the version you are
  running either: the comparison happens on your PC.
- A downloaded update is checked against the SHA-256 published with that release before it is run,
  and deleted if it does not match. Update links are accepted only from GitHub, only over HTTPS,
  and a redirect anywhere else is refused rather than followed.
- Releases are **not code-signed**, so Windows SmartScreen shows a warning on first run. Verify the
  SHA-256 checksum against `SHA256SUMS.txt` before running anything you download. See
  [SECURITY.md](SECURITY.md).

### Notes on how this was built

I built the first version with AI coding agents, then went through the result myself before
releasing it. That turned up seven defects the tests had not caught: one that could leave the
window permanently unresponsive, with Task Manager the only way out; a "Show in Folder" button
that opened the wrong folder and reported success either way; and a leak that grew every time the
imaging work landed on a new thread. The test suite went from 23 to 28 in the process.

An eighth turned up later, and it is the one I would have hated to ship: the released binary
embedded the full path it was built from, so anyone who ran `strings` on a download would have
seen the account name and folder layout of the machine that produced it. No checksum or provenance
attestation catches that, because both will happily attest a file that leaks. The build now refuses to
package a binary containing that path.

I would rather have that written down than not.

[1.1.0]: https://github.com/CourageToChange/LocalPhotoPDF/releases/tag/v1.1.0
[1.0.0]: https://github.com/CourageToChange/LocalPhotoPDF/releases/tag/v1.0.0
