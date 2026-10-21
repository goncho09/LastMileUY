
self.importScripts('./service-worker-assets.js');

const cacheName = `lastmileuy-driver-${self.assetsManifest.version}`;

const assetsToCache = self.assetsManifest.assets
    .filter(asset =>
        !asset.url.endsWith('.map') &&
        !asset.url.endsWith('.br') &&
        !asset.url.endsWith('.gz') &&
        !asset.url.endsWith('service-worker.js') &&
        !asset.url.endsWith('service-worker.published.js'))
    .map(asset => new Request(asset.url, { cache: 'no-cache' }));

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(cacheName)
            .then(cache => cache.addAll(assetsToCache))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(keys =>
            Promise.all(
                keys
                    .filter(key =>
                        key.startsWith('lastmileuy-driver-') &&
                        key !== cacheName)
                    .map(key => caches.delete(key))
            )
        ).then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    const request = event.request;
    const url = new URL(request.url);

    if (request.method !== 'GET' || url.origin !== self.location.origin)
        return;

    if (url.pathname.startsWith('/api/'))
        return;

    if (request.mode === 'navigate') {
        event.respondWith(
            fetch(request).catch(async () => {
                const cache = await caches.open(cacheName);
                return await cache.match('index.html') ||
                    Response.error();
            })
        );
        return;
    }

    event.respondWith(
        caches.match(request).then(cached =>
            cached || fetch(request)
        )
    );
});
