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

- processes photos locally and has no application network features;
- uses Windows image codecs rather than executing image files;
- limits file size, decoded dimensions, and batch size;
- writes a temporary PDF, validates it, and only then replaces the requested destination;
- does not add startup entries, file associations, shell extensions, services, or update agents;
- restores dependencies from committed lock files in automated builds; and
- uses Dependabot for dependency updates and CodeQL for static analysis.

No software can be guaranteed bug-free or perfectly safe. Treat untrusted image files cautiously and keep Windows and installed image codecs updated.

## Unsigned releases

Initial LocalPhotoPDF releases are not code-signed. This does not make SmartScreen's reputation warning proof of malware or proof of safety. Download only from this repository's GitHub Releases page, verify `SHA256SUMS.txt`, and check the release's GitHub build provenance attestation before running it.
