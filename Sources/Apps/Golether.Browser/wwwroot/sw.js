// The service worker of Golether: it answers the <video> element of a guest for stream/<number> with the bytes of
// the film, which the page reads from the cache of the session, piece by piece from the host. It is the browser's
// counterpart of the golether:// protocol of the desktop player. Nothing is stored: every answer is streamed.

const PART = 256 * 1024;
const TIMEOUT = 60000;

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()));

self.addEventListener('fetch', (e) => {
  const url = new URL(e.request.url);
  const match = /\/stream\/(\d+)$/.exec(url.pathname);
  if (url.origin === self.location.origin && match) {
    e.respondWith(serve(e, Number(match[1])));
  }
});

/** Asks the page and waits for its answer. */
function ask(client, message) {
  return new Promise((resolve, reject) => {
    const channel = new MessageChannel();
    const timer = setTimeout(() => reject(new Error('Timeout')), TIMEOUT);
    channel.port1.onmessage = (e) => {
      clearTimeout(timer);
      resolve(e.data);
    };
    client.postMessage(message, [channel.port2]);
  });
}

/** Answers a request of the element, honouring the range it asked for. */
async function serve(e, stream) {
  const client = (await self.clients.get(e.clientId)) ?? (await self.clients.matchAll({ type: 'window' }))[0];
  if (!client) {
    return new Response(null, { status: 503 });
  }

  const { length, type } = await ask(client, { type: 'length', stream });
  if (!(length >= 0)) {
    return new Response(null, { status: 404 });
  }

  const range = /^bytes=(\d+)-(\d*)$/.exec(e.request.headers.get('range') ?? '');
  const start = range ? Number(range[1]) : 0;
  const end = range && range[2] ? Math.min(Number(range[2]), length - 1) : length - 1;
  if (start >= length || start > end) {
    return new Response(null, { status: 416, headers: { 'Content-Range': `bytes */${length}` } });
  }

  let offset = start;
  const body = new ReadableStream({
    async pull(controller) {
      if (offset > end) {
        controller.close();
        return;
      }
      try {
        const answer = await ask(client, { type: 'read', stream, offset, count: Math.min(PART, end - offset + 1) });
        if (!answer.bytes || answer.bytes.byteLength === 0) {
          controller.error(new Error('Read'));
          return;
        }
        offset += answer.bytes.byteLength;
        controller.enqueue(new Uint8Array(answer.bytes));
      } catch (error) {
        controller.error(error);
      }
    },
  });

  const headers = {
    'Content-Type': type || 'application/octet-stream',
    'Accept-Ranges': 'bytes',
    'Content-Length': String(end - start + 1),
    'Cache-Control': 'no-store',
  };
  if (range) {
    headers['Content-Range'] = `bytes ${start}-${end}/${length}`;
  }
  return new Response(body, { status: range ? 206 : 200, headers });
}
