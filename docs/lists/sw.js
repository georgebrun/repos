// BoE Lists: keep the page itself on the phone so it opens without signal.
// The lists live in the page's own storage; card pictures come from Scryfall when online.
const CACHE = "boe-lists-page-v2";
const FILES = ["./", "index.html", "manifest.webmanifest", "icon-180.png", "icon-192.png", "icon-512.png"];

self.addEventListener("install", e => {
  e.waitUntil(caches.open(CACHE).then(c => c.addAll(FILES)).then(() => self.skipWaiting()));
});
self.addEventListener("activate", e => {
  e.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))
    .then(() => self.clients.claim()));
});
// The page: newest when online, the kept copy when not. Anything else (pictures): straight to the web.
self.addEventListener("fetch", e => {
  const url = new URL(e.request.url);
  if (url.origin !== location.origin || e.request.method !== "GET") return;
  // Back from the Dropbox sign-in (?code=…): kept as the plain page, not one copy per sign-in.
  const key = url.search ? new Request(url.origin + url.pathname) : e.request;
  e.respondWith(fetch(e.request).then(r => {
    if (r.ok) { const copy = r.clone(); caches.open(CACHE).then(c => c.put(key, copy)); }
    return r;
  }).catch(() => caches.match(key).then(m => m || caches.match("index.html"))));
});
