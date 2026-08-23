# NexaPlay

**Made by SauceBoyz**

NexaPlay is a native Windows game catalog, downloader, archive installer, and launcher built for private groups and small creators distributing games they have permission to share.

## What it does

- Professional gamer-focused WPF library with 12-game numbered pages, a compact filter menu, cinematic featured game, live catalog status, search, installed filtering, and favorites.
- In-app game pages with descriptions, tags, multiplayer status, clickable five-star ratings, screenshots, trailer/gameplay links, and update age.
- Steam-inspired Downloads page with real network/peak speed, transfer progress and ETA, a sequential **Up Next** queue, cancellation, and persistent download history with a clear option.
- Automatic safe guide/notes defaults, Steam minimum/recommended requirements, and a reusable **Can I run it?** PC profile for CPU, GPU, RAM, Windows, and storage.
- In-app ratings and problem-report forms. With the Community server connected, friends share one vote per Player installation and Owner Studio receives private reports.
- Separate private Owner Studio with instant title/developer/publisher/App-ID/tag search, missing-artwork filtering, selected artwork repair, and catalog-wide broken-cover repair. It is never included in the player installer.
- One-click catalog-wide metadata refresh for existing games without changing download links, versions, guides, or package settings.
- Optional SteamGridDB cover/hero lookup using your own API key.
- Resumable HTTP/HTTPS downloads with progress and cancellation.
- Interrupted-install recovery reuses a completed archive, and extraction shows live bytes for the current file instead of appearing frozen.
- Installed games can be safely uninstalled from their game page. NexaPlay verifies the game manifest, permanently deletes only the managed game folder, refreshes immediately, and changes Play back to the download flow.
- ZIP, RAR, and 7z extraction through SharpCompress.
- `Run Me!.bat` is always the preferred launcher; a configured or detected executable is used only as a fallback.
- Full Game, Update, Online Fix, and one owner-named custom package work independently. Use the custom package for a language pack, DLC helper, or another optional archive; it stays hidden when unset.
- Optional SHA-256 verification before extraction.
- Archive path-traversal protection and rejection of HTML landing pages posing as downloads.
- Per-user Windows installer, Start Menu shortcut, optional desktop shortcut, upgrades, and uninstall.
- Built-in app update checks through a small HTTPS manifest. Updates are never silently installed: NexaPlay shows the available version, asks before downloading, shows speed/ETA, verifies SHA-256, and asks again before opening the normal installer.
- First-run quick-start guide with a Skip button, plus an editable local player name and optional profile picture.

Only distribute content you own or are authorized to redistribute. NexaPlay does not scrape games, bypass host protections, or provide copyrighted downloads.

## Player app and private Owner Studio

Friends receive only `NexaPlay-Setup-v1.10.0.exe`. The player starts with the bundled catalog, has no Creator Studio or Game Editor navigation, and cannot publish catalog changes.

Keep the latest `NEXAPLAY - USE THESE\NexaPlay-Owner-Studio-*.exe` private. It uses the GitHub CLI account already signed in on the owner PC, so no owner key is required for GitHub catalog publishing. Add, edit, remove, auto-fill, import, and metadata refresh save to the separate Owner draft automatically. **Save** confirms a local save, **Publish** updates `catalog/nexaplay-catalog.json` on GitHub, and **Done** safely saves before closing.

## Your first catalog

1. Open the private **NexaPlay Owner Studio** and choose **+ Add Games**.
2. Enter the game title and choose **Auto-Fill Game**. NexaPlay finds the Steam App ID and builds the metadata portion of the page.
3. Paste the direct game archive URL. The URL can be long and does not need to contain the game name.
4. Optionally paste separate update, Online Fix, and owner-named custom-package archive links. Empty package types stay hidden from the public game page. File sizes accept B, KB, MB, GB, or TB, and **Detect sizes** asks the host automatically.
5. In Owner Studio, choose **SteamGridDB key…**, open the official API key page, and save your key. It is encrypted for your Windows account and is never shipped to players. Friends receive the resulting artwork URLs automatically and do not need a key.
6. Put `Run Me!.bat` in the game archive. NexaPlay launches it automatically; the executable field is only a fallback.
7. Add SHA-256 checksums whenever possible, then save the game. Owner Studio saves it automatically. Click **Publish** when it is ready for friends; their Player apps check the catalog automatically about every 30 seconds. Click **Done** whenever you want to save and close Studio.

The included `server` folder supplies the real shared catalog, ratings, and reports API. It stores one current vote per player ID, keeps reports private to Owner Studio, and requires the private owner key for catalog/report access. Host it behind HTTPS and keep `NEXAPLAY_ADMIN_KEY` only on the server and owner machine.

Until that server has a public HTTPS address, friends still receive every game bundled in the installer, but future additions require either a new installer or a configured remote catalog. Ratings are saved locally and clearly marked as local until the community service is connected.

## GitHub and automatic app updates

NexaPlay uses public GitHub raw files for the live catalog and `update-manifest.json`, plus a GitHub Release asset for the installer. Friends never need a GitHub account or token. After building, run `prepare-update-release.ps1 -InstallerUrl <HTTPS installer asset URL>` to create the manifest with the real installer SHA-256.

Catalog changes do not require an app update: players sync additions, removals, links, metadata, and artwork automatically every 10 seconds and immediately when NexaPlay regains focus. Existing settings are migrated back to live sync automatically. A newer NexaPlay app version changes the sidebar indicator to **Update v… ready**. Nothing is silently installed: the player approves the download and separately approves opening the normal visible installer.

### Gofile and other file hosts

NexaPlay needs a direct file-download response. A normal share page often returns HTML and will be rejected with a clear message. Copy the host's direct download URL when available. Hosts that require a browser challenge, login, CAPTCHA, or expiring session token are not reliable direct-download sources.

## Local data and privacy

- Settings, player profile picture, persistent download history, the Player cache, and the separate Owner draft catalog: `%LOCALAPPDATA%\NexaPlay`
- Default game library: `%USERPROFILE%\Documents\NexaPlay Library`
- SteamGridDB key and private owner key: encrypted separately with Windows Data Protection for the current user
- Player installer payload: player app, bundled catalog, and runtime only

The player installer does **not** include your API keys, owner key, Owner Studio, local settings, library path, archives, installed games, or preferences. It does include `default-catalog.json`, so a new friend immediately sees the games that were bundled at build time. Uninstall removes the program and shortcuts but intentionally keeps user data and games.

## Build and verify

```powershell
dotnet build .\NexaPlay.slnx -c Release
dotnet run --project .\NexaPlay.SmokeTests\NexaPlay.SmokeTests.csproj -c Release
Push-Location .\server; npm test; Pop-Location
.\build-owner.ps1
.\build-installer.ps1
```

Both current EXEs are written to the top-level `NEXAPLAY - USE THESE` folder. Share only `NexaPlay-Setup-v1.10.0.exe`; keep the Owner Studio private. The installer is unsigned, so Windows SmartScreen may show **Unknown publisher**. Removing that warning requires a trusted code-signing certificate.
