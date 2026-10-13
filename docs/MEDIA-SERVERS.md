# Media server browsing

Media Servers opens each connected server's video libraries separately. The server's names and order are retained; Continue Watching and Next Up use its own endpoints. Open View all for server search, genres, sorting and page navigation. Collections open as folders. Playback returns through Back to server to the same browser.

Jellyfin and Emby discovery uses user Views, then Items with ParentId. AIOStreams' built-in Jellyfin layer exposes addon catalogs this way. A library marked aiostreams.genreRequired must receive a genre: NullTV uses None as All genres where offered, otherwise the first real genre. A short response does not end pagination when TotalRecordCount says more titles remain. Catalog previews are loaded with at most four concurrent library reads. They are cached locally in the encrypted profile cache; View all loads additional pages on demand.

Browsing and details omit MediaSources so virtual servers do not perform stream lookups for every poster. PlaybackInfo is requested on Play, using the server's exact request-field casing. Multiple versions are selectable using server-supplied labels. Version/session IDs are retained for playback reporting. A server token is appended only to a stream URL on that server's origin, never an external stream host. Unsupported optional home endpoints do not erase accessible libraries; authentication and malformed library responses fail without overwriting the cached catalog.

Plex library sections retain their separate titles and browsing views. The existing Plex direct-play and watched/progress sync remains in place. This release concerns video libraries, rather than music or books.

Protocol references: [AIOStreams Jellyfin extensions](https://docs.aiostreams.viren070.me/reference/jellyfin-extensions/), [AIOStreams library routes](https://github.com/Viren070/AIOStreams/blob/main/packages/server/src/routes/jellyfin/library.ts), [Jellyfin web home sections](https://github.com/jellyfin/jellyfin-web/blob/master/src/components/homesections/homesections.js).
