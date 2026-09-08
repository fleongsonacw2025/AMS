self.addEventListener('fetch', event => {
    // Simple pass-through for now, but required for PWA installation
    event.respondWith(fetch(event.request));
});