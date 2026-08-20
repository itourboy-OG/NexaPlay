# NexaPlay

**Made by SauceBoyz**

NexaPlay is a native Windows game catalog, downloader, archive installer, and launcher built for private groups and small creators distributing games they have permission to share.

## What it does

- Professional gamer-focused WPF library with a sharp launcher sidebar, cinematic featured game, animated cover cards, live catalog status, search, installed filtering, favorites, and working Multiplayer/Co-op/Recently Updated filters.
- In-app game pages with descriptions, tags, multiplayer status, clickable five-star ratings, screenshots, trailer/gameplay links, and update age.
- Dedicated Downloads page with live transfer speed, bytes transferred, time remaining, estimated completion time, install phase, cancellation, and finished history.
- Automatic safe guide/notes defaults, Steam minimum/recommended requirements, and a reusable **Can I run it?** PC profile for CPU, GPU, RAM, Windows, and storage.
- Report button that opens a configured support form or pre-filled email; if neither is configured it copies a report template.
- Separate private Owner Studio: enter a title and links, then Auto-Fill finds the Steam App ID and fills descriptions, genres, multiplayer tags, ratings, requirements, screenshots, trailer, and artwork. It is never included in the player installer.
- One-click catalog-wide metadata refresh for existing games without changing download links, versions, guides, or package settings.
- Optional SteamGridDB cover/hero lookup using your own API key.
- Resumable HTTP/HTTPS downloads with progress and cancellation.
- Interrupted-install recovery reuses a completed archive, and extraction shows live bytes for the current file instead of appearing frozen.
- ZIP, RAR, and 7z extraction through SharpCompress.
- `Run Me!.bat` is always the preferred launcher; a configured or detected executable is used only as a fallback.
- Full Game, Update, and Online Fix downloads work independently. Updates and fixes can be applied to an existing game folder even when NexaPlay did not install the full game.
- Optional SHA-256 verification before extraction.
- Archive path-traversal protection and rejection of HTML landing pages posing as downloads.
- Per-user Windows installer, Start Menu shortcut, optional desktop shortcut, upgrades, and uninstall.
- Built-in app update checks through a small HTTPS manifest. Updates are never silently installed: NexaPlay shows the available version, asks before downloading, shows speed/ETA, verifies SHA-256, and asks again before opening the normal installer.
- First-run quick-start guide with a Skip button, plus an editable local player name and optional profile picture.

Only distribute content you own or are authorized to redistribute. NexaPlay does not scrape games, bypass host protections, or provide copyrighted downloads.

## Player app and private Owner Studio

Friends receive only `NexaPlay-Setup-v1.9.0.exe`. The player starts with the bundled catalog, has no Creator Studio or Game Editor navigation, and cannot publish catalog changes.

Keep the latest `NEXAPLAY - USE THESE\NexaPlay-Owner-Studio-*.exe` private. It uses the GitHub CLI account already signed in on the owner PC, so no owner key is required for GitHub catalog publishing. Add, edit, remove, auto-fill, import, and metadata refresh save to the separate Owner draft automatically. **Save** confirms a local save, **Publish** updates `catalog/nexaplay-catalog.json` on GitHub, and **Done** safely saves before closing.

## Your first catalog

1. Open the private **NexaPlay Owner Studio** and choose **+ Add Games**.
2. Enter the game title and choose **Auto-Fill Game**. NexaPlay finds the Steam App ID and builds the metadata portion of the page.
3. Paste the direct game archive URL. The URL can be long and does not need to contain the game name.
4. Optionally paste separate update and Online Fix archive links. Empty package types stay hidden from the public game page.
5. In Owner Studio, choose **SteamGridDB key…**, open the official API key page, and save your key. It is encrypted for your Windows account and is never shipped to players. Friends receive the resulting artwork URLs automatically and do not need a key.
6. Put `Run Me!.bat` in the game archive. NexaPlay launches it automatically; the executable field is only a fallback.
7. Add SHA-256 checksums whenever possible, then save the game. Owner Studio saves it automatically. Click **Publish** when it is ready for friends; their Player apps check the catalog automatically about every 30 seconds. Click **Done** whenever you want to save and close Studio.

The included `server` folder supplies the real shared catalog and ratings API. It stores one current vote per player ID and requires the private owner key for catalog writes. Host it behind HTTPS, put its `/api/catalog` URL in the player catalog settings, and keep `NEXAPLAY_ADMIN_KEY` only on the server and owner machine.

Until that server has a public HTTPS address, friends still receive every game bundled in the installer, but future additions require either a new installer or a configured remote catalog. Ratings are saved locally and clearly marked as local until the community service is connected.

## GitHub and automatic app updates

NexaPlay uses public GitHub raw files for the live catalog and `update-manifest.json`, plus a GitHub Release asset for the installer. Friends never need a GitHub account or token. After building, run `prepare-update-release.ps1 -InstallerUrl <HTTPS installer asset URL>` to create the manifest with the real installer SHA-256.

Catalog changes do not require an app update: players sync additions, removals, links, metadata, and artwork automatically. A newer NexaPlay app version changes the sidebar indicator to **Update v… ready**. Nothing is silently installed: the player approves the download and separately approves opening the normal visible installer.

### Gofile and other file hosts

NexaPlay needs a direct file-download response. A normal share page often returns HTML and will be rejected with a clear message. Copy the host's direct download URL when available. Hosts that require a browser challenge, login, CAPTCHA, or expiring session token are not reliable direct-download sources.

## Local data and privacy

- Settings, player profile picture, the Player cache, and the separate Owner draft catalog: `%LOCALAPPDATA%\NexaPlay`
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

Both current EXEs are written to the top-level `NEXAPLAY - USE THESE` folder. Share only `NexaPlay-Setup-v1.9.0.exe`; keep the Owner Studio private. The installer is unsigned, so Windows SmartScreen may show **Unknown publisher**. Removing that warning requires a trusted code-signing certificate.
