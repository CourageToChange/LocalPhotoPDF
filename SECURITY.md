# Security Policy

## Supported versions

Security fixes are made for the latest released version of LocalPhotoPDF. Older releases may not receive updates.

| Version | Supported |
| --- | --- |
| Latest release | Yes |
| Older releases | No |

## Report a vulnerability privately

Please do not open a public issue for a suspected vulnerability.

Use GitHub's **Report a vulnerability** option on this repository's **Security** tab. Include the affected version, Windows version, impact, and the smallest safe set of reproduction steps. Do not upload private photos, PDFs, credentials, personal file paths, or other sensitive data. A synthetic sample image is preferable when a file is required to reproduce the problem.

If private vulnerability reporting is not available, contact a maintainer privately through the contact method listed on their GitHub profile and ask for a secure reporting channel before sending details.

## Security design

LocalPhotoPDF:

- processes photos locally, and uses the network only for the update check you start by pressing the button;
- uses Windows image codecs rather than executing image files;
- limits file size, decoded dimensions, and batch size;
- writes a temporary PDF, validates it, and only then replaces the requested destination;
- does not add startup entries, file associations, shell extensions, services, or anything that updates in the background;
- restores dependencies from committed lock files in automated builds; and
- uses Dependabot for dependency updates and CodeQL for static analysis.

No software can be guaranteed bug-free or perfectly safe. Treat untrusted image files cautiously and keep Windows and installed image codecs updated.

## Known limitations we have accepted

**A program already running on your PC can stop LocalPhotoPDF starting.** The app uses a named
Windows mutex so a second copy cannot open while the first is running, and the installer waits on
the same one so it never replaces files that are in use. That name is fixed and, because this
project is open source, publicly known. Any other program running under your Windows account can
hold it and make LocalPhotoPDF refuse to start, and make an update refuse to install.

We have accepted this rather than fixed it. Closing it properly needs either administrator rights,
which this app deliberately never asks for, or a different single-instance mechanism that would be
more code to get wrong. Anything able to exploit it is already running as you and already has far
more damaging options available. It is written down here because it is a real limitation and you
should hear it from us rather than discover it.

**A verified update is re-checked before it runs.** The installer is hashed when it finishes
downloading, but it then waits in a temporary folder until you press Install, which may be much
later. That folder is writable by anything running as you. LocalPhotoPDF therefore hashes the file
again immediately before starting it, and refuses to run it if it has changed.

## Unsigned releases

Initial LocalPhotoPDF releases are not code-signed. This does not make SmartScreen's reputation warning proof of malware or proof of safety. Download only from this repository's GitHub Releases page, verify `SHA256SUMS.txt`, and check the release's GitHub build provenance attestation before running it.
