# Privacy

Last updated: 2026-08-15

LocalPhotoPDF is designed to process photos entirely on your Windows PC.

## Data the app uses

When you add photos, the app reads those files so it can show previews and make the PDF you requested. It writes the finished PDF only to the destination you choose. Source photos are not changed.

The app does not:

- upload photos or PDFs;
- require an account;
- include adverts or analytics;
- send telemetry or crash reports;
- keep a recent-file or conversion history; or
- contact the internet on its own, at any time, for any reason.

LocalPhotoPDF may save non-sensitive preferences, such as your page size, margin, and quality choices, under your Windows local application-data folder. It does not intentionally save source-photo paths in those preferences. The per-user uninstaller removes the app's saved preferences.

## Checking for updates

Version 1.0.0 added a **Check for updates** button along the bottom of the window. It is the only part of LocalPhotoPDF that uses the internet, and it runs only when you press it. There is no timer, no check when the app starts, and no background poll. If you never press it, the app never makes a network request.

When you do press it, the app asks GitHub for the details of the newest published release. GitHub sees the same things any website sees when you visit it: your IP address, and that the request came from a LocalPhotoPDF update check. Nothing is sent about you, your PC, your photos, or how you use the app. There is no account, no licence key and no installation identifier, and the app does not send the version you are running either, because the comparison happens on your PC. Apart from the IP address it comes from, one check looks exactly like another.

If a newer version exists, the app tells you and asks before downloading anything. If you agree, it downloads the installer, checks it against the SHA-256 checksum published with that release, and refuses to run it if they do not match. You then press a second time to install.

If you would rather not use the button at all, every release is also on the [releases page](https://github.com/CourageToChange/LocalPhotoPDF/releases) and you can download it in your browser instead. The app works exactly the same either way.

## Metadata

The PDF contains rendered versions of the photos you selected. LocalPhotoPDF does not copy source EXIF, camera, or GPS metadata into the output. As with any document, visible information inside a photo remains visible in the PDF.

## Windows components

The app uses image codecs installed in Windows to decode files. Optional codecs are separate software governed by their own privacy terms. LocalPhotoPDF does not install or contact those codecs for you.

## Downloads and GitHub

Downloading LocalPhotoPDF, opening its GitHub repository, or reporting an issue involves GitHub and your browser, not the LocalPhotoPDF app. Their own privacy terms apply.

## Questions

Open a GitHub issue for a general privacy question. Please keep photos, PDFs and file paths out of a public issue. Describe them instead. Follow [SECURITY.md](SECURITY.md) for a potential vulnerability.
