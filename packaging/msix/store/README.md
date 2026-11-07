# Microsoft Store listing

Everything Partner Center needs besides the package. Store ID `9P8H93Z0TGK4`.

## Files

- `listings.json`: listing text for all 9 package languages (description, what's new, features, search terms, screenshot headlines).
- `listing-template.csv`: the empty template from Partner Center > Store listings > Export listings. The script fills it in.
- `ScreenshotTool/`: renders the real main window in each language at 2.5x, with a headline, on a 1920x1080 image.
- `make-listings.ps1`: builds the screenshots and the CSV files into `Symlinker/bin/store-listing`.

## Updating the listing

1. Edit `listings.json`. Every submission needs new "What's new" text (`ReleaseNotes`) in every language.
2. Run `./packaging/msix/store/make-listings.ps1`.
3. In Partner Center, open the submission, then Store listings > Import listings > Upload .csv and pick `listingData-text-only.csv`.
4. Screenshots stay from the last submission. To replace them, upload `<language>/store1-3.png` on each language's listing page.

Upload folder with `listingData.csv` would do both at once, but it failed with an empty error list from Claude's built-in browser. Use Chrome for that, or the steps above.

## Rules Partner Center enforces

- The product name (`Title`) must be a reserved name: "Symlinker - Symlink Creator". "Symlinker" alone is taken.
- Every language the package declares needs a complete listing with at least one screenshot.
- Full trust apps need a privacy policy URL, even when the app collects nothing. Under Properties, the URL field only shows with "Yes, my product uses personal information": https://amd989.github.io/Symlinker/privacy.html

## Restricted capabilities (Submission options)

```
runFullTrust: Symlinker is a WPF desktop app packaged as a full-trust desktop app. It runs the Windows mklink command (cmd.exe) to create symbolic links, hard links and directory junctions at paths the user chooses, which is not possible from an AppContainer.

allowElevation: Windows requires administrator rights to create symbolic links unless Developer Mode is on. The app itself runs unelevated. Only when the user creates a symbolic link that needs admin, it starts a hidden helper with the standard UAC prompt, and the helper only runs mklink on the paths the user entered. It makes no other system changes.

unvirtualizedResources: Users create links in any folder they choose, including under AppData. With write virtualization on, links created there would silently land in the package's private folder instead of the real location the user picked, so the app turns off file and registry write virtualization.
```

## Notes for certification (Additional Testing Information)

```
No account or sign-in is needed. The app starts without administrator rights.

To test: choose Folder, choose Symbolic link, set the link location to any folder (e.g. C:\Users\Public), type a link name, set the target to any existing folder, then click Create link. Unless Developer Mode is on, Windows shows a UAC prompt; please accept it. The new link appears in File Explorer with a shortcut-arrow icon and opens the target folder. Junction (folders) and Hard link (files) work the same way and never ask for permission.

The app makes no network connections. Updates come through the Microsoft Store.
```
