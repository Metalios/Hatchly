// One-time recovery worker for earlier Hatchly PWA builds.
// Existing cached pages still request this URL. Updating to this worker removes
// the old cache-first worker, clears its caches, and reloads open Hatchly tabs.
self.addEventListener("install", event => {
    event.waitUntil(self.skipWaiting());
});

self.addEventListener("activate", event => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(
            keys
                .filter(key => key.startsWith("offline-cache-"))
                .map(key => caches.delete(key)));

        await self.clients.claim();
        await self.registration.unregister();

        const windows = await self.clients.matchAll({ type: "window" });
        await Promise.all(windows.map(client => client.navigate(client.url)));
    })());
});

self.addEventListener("fetch", event => {
    event.respondWith(fetch(event.request));
});
