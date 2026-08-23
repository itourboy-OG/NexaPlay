# NexaPlay v1.11.2

This release makes expired and broken game-package links easy to find before anyone wastes time starting a download.

- Player checks every configured package when a game page opens.
- Working links are shown in green; expired, missing, blocked, or invalid links are shown in red with the HTTP status.
- Temporary network or host problems are shown separately in amber so they are not mistaken for permanently broken links.
- HTTP 410 Gone now explains that the signed catalog link expired and needs to be replaced by SauceBoyz.
- Creator Studio automatically scans the catalog, shows a broken-link count, and can filter directly to games that need repairs.
- Creator Studio includes a manual **Check links** action for the full catalog and for individual games in the editor.
- Link checks use a one-byte ranged request and do not download the game archive.

Made by SauceBoyz.
