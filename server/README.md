# NexaPlay Community service

This small service is the shared source of truth for the player catalog and community ratings. The public Windows app can read the catalog and submit one rating per local player ID. Only requests carrying the private `NEXAPLAY_ADMIN_KEY` can replace the catalog.

It must be hosted behind HTTPS before it is enabled in a shared catalog. Keep the admin key on the SauceBoyz owner machine/server only; never put it in the public installer or catalog JSON.

Environment variables:

- `PORT` — local listening port, default `3214`
- `NEXAPLAY_DATA_DIR` — persistent server data folder
- `NEXAPLAY_ADMIN_KEY` — long private owner key required for catalog writes

Run `npm test` before deployment and `npm start` to launch it locally. Put an HTTPS reverse proxy or tunnel in front of the local port, then set the catalog's `communityApiUrl` to that public base URL.
