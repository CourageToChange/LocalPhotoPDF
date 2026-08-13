# LocalPhotoPDF User Guide

Last updated: 2026-08-09

LocalPhotoPDF turns photos on your Windows PC into one PDF. Everything happens on your computer. You do not need an account or an internet connection.

## Open LocalPhotoPDF

If you used the installer, open the Start menu and select **LocalPhotoPDF**. If you downloaded the portable ZIP, extract it first and then open **LocalPhotoPDF.exe**.

The first releases are not digitally signed. Windows SmartScreen may show an "unrecognized app" message even when the file is unchanged. Only use a release from this project's GitHub Releases page and check its SHA-256 value against `SHA256SUMS.txt`.

## Make a PDF

1. Select **Add Photos** or click the drop zone, then choose one or more image files. You can also drag image files onto the window.
2. Check the preview list. Its top-to-bottom order becomes the PDF's page order.
3. Reorder photos by dragging their rows, or select a photo and use the **↑** (move up) or **↓** (move down) button.
4. Use the **↶** (rotate left), **↷** (rotate right), or **×** (remove) button when needed. **Clear All** removes every photo from the list.
5. Choose a **Page size**, **Margin**, and **Quality**. The defaults are a good choice for most documents.
6. Select **Generate PDF**, choose a file name and destination, and select **Save**.
7. Wait for the progress bar to finish. You can select **Cancel** while the PDF is being made.
8. Select **Open PDF** to view the result or **Show in Folder** to find it in File Explorer.

LocalPhotoPDF writes to a temporary file first. If you cancel or the conversion fails, it tries again to tidy up the temporary file and leaves an existing PDF at the destination unchanged. If Windows still prevents removal, the app shows the temporary file's exact path so you can delete it; that file may contain your photos.

## Choose page settings

### Page size

- **A4** is the standard page size in the UK and most of the world. Each page automatically uses portrait or landscape orientation to suit the photo.
- **US Letter** makes standard North American document pages and also chooses portrait or landscape automatically.
- **Match each photo** makes each PDF page match that photo's shape.

Photos are fitted in the available page area and centred. LocalPhotoPDF does not crop them. Empty space is white.

### Margin

Choose **None**, **5 mm**, or **10 mm**. The default is **5 mm**. A larger margin adds more white space around each photo.

### Quality

- **High** keeps more image detail and usually makes a larger PDF.
- **Balanced** is the default and suits most photos and screenshots.
- **Small** reduces the PDF size more strongly.

## Supported photos

LocalPhotoPDF supports JPEG, PNG, BMP, GIF, TIFF, and JPEG XR through Windows. It can also use optional Windows codecs installed for formats such as HEIC, WebP, AVIF, and camera RAW.

Only the first frame of an animated GIF or multi-page TIFF is used. The app corrects stored camera orientation and applies any rotation you choose. Source photo metadata, including GPS location, is not copied into the PDF.

There are limits on what you can add. One conversion can contain up to 500 photos, a source file can be up to 250 MiB, and an image can be up to 200 megapixels. Photos beyond these limits are not added. Large batches and very high-resolution photos can still use substantial memory while they are open.

## Messages you may see

- **Unsupported format or missing codec:** Windows cannot decode that image. Install the matching image extension from the Microsoft Store, or convert the image to JPEG or PNG, and add it again.
- **Duplicate photo:** That file is already in the list. Each source file can be added once.
- **File is too large:** Resize the photo or save a smaller copy before adding it.
- **File moved or missing:** Put the file back in its original location or remove it and add it again.
- **Cannot write the PDF:** Choose a folder where you can save files. If the destination PDF is open in another app, close it and try again.
- **Not enough free space:** Free some disk space or choose another destination.

A problem with one file should not close the app. Read the on-screen message, remove or replace the affected file, and try again.

## Keyboard shortcuts

- Press **Ctrl+O** to add photos.
- Press **Ctrl+G** to generate the PDF when photos are ready.
- Press **Alt+Up Arrow** or **Alt+Down Arrow** to move the selected photo.
- Press **Delete** to remove the selected photo.
- Press **Escape** to cancel PDF creation safely.

## Privacy and cleanup

The app does not upload photos, keep a recent-file history, or send telemetry. It only remembers your window size and your page, margin and quality choices.

Uninstall LocalPhotoPDF from **Settings > Apps > Installed apps**. The uninstaller removes the installed program, shortcuts, and saved preferences. Portable users can remove the extracted folder manually. To remove portable-use preferences too, open File Explorer, paste `%LOCALAPPDATA%` into the address bar, press Enter, and delete the `LocalPhotoPDF` folder.

For more detail, read the [Privacy policy](PRIVACY.md). To report a security problem, follow the [Security policy](SECURITY.md).
