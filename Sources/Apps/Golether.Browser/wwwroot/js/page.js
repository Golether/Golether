// The page around the canvas: the servers it was published with, the invitation in its address, the settings of this
// browser, the clipboard, the full screen, the sleep of the display and the bytes of the film the service worker asks
// for. .NET decides; this module only holds the objects of the browser.

const PREFIX_KEYS = /^golether\./;

let configText = '{}';
let streams = null;
let pageCallbacks = null;
let sounds = null;
let wakeLock = null;
let wantAwake = false;
const reads = new Map();
let nextRead = 1;

/** Keeps the text of config.json for .NET. */
export function setConfig(text) {
  configText = text;
}

/** Takes the exports of .NET that answer the service worker and hear about the page, and starts listening. */
export function bind(streamCallbacks, callbacks) {
  streams = streamCallbacks;
  pageCallbacks = callbacks;
  navigator.serviceWorker?.addEventListener('message', onWorkerMessage);
  document.addEventListener('visibilitychange', applyWake);
  document.addEventListener('fullscreenchange', () => pageCallbacks?.OnFullScreenChanged());
  // A browser lets a page make sounds only after a click; the first click wakes the sounds of the page.
  document.addEventListener('pointerdown', () => soundContext(), { capture: true });
}

/** The audio context of the short sounds, made and woken on demand. */
function soundContext() {
  if (!sounds) {
    const Context = window.AudioContext ?? window.webkitAudioContext;
    if (!Context) {
      return null;
    }
    sounds = new Context();
  }
  if (sounds.state !== 'running') {
    sounds.resume().catch(() => {});
  }
  return sounds;
}

/** Plays a short sound given as a WAV file. */
export function playSound(wave) {
  const context = soundContext();
  if (!context) {
    return;
  }
  const bytes = wave.slice();
  context.decodeAudioData(bytes.buffer).then((buffer) => {
    const source = context.createBufferSource();
    source.buffer = buffer;
    source.connect(context.destination);
    source.start();
  }).catch(() => {});
}

/** Whether the page is in full screen now. */
export function isFullScreen() {
  return Boolean(document.fullscreenElement);
}

/** The text of config.json. */
export function config() {
  return configText;
}

/** Takes the invitation after the # and removes it from the address bar, so it stays out of the history. */
export function takeInvitation() {
  const hash = location.hash.slice(1);
  if (hash) {
    history.replaceState(null, '', location.pathname + location.search);
  }
  return hash;
}

/** The address of the page without its fragment. */
export function address() {
  return location.origin + location.pathname;
}

/** Reads a setting of this browser. */
export function getSetting(key) {
  if (!PREFIX_KEYS.test(key)) {
    return null;
  }
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

/** Stores a setting of this browser. */
export function setSetting(key, value) {
  if (!PREFIX_KEYS.test(key)) {
    return;
  }
  try {
    localStorage.setItem(key, value);
  } catch {
    // A browser without storage (private mode of some browsers) forgets the settings with the page.
  }
}

/** Puts a text on the clipboard. */
export async function copy(text) {
  await navigator.clipboard.writeText(text);
}

/** Reads the text on the clipboard, or an empty one when the browser refused. */
export async function paste() {
  try {
    return (await navigator.clipboard?.readText()) ?? '';
  } catch {
    return '';
  }
}

/** Enters or leaves the full screen of the whole page. */
export function toggleFullScreen() {
  if (document.fullscreenElement) {
    document.exitFullscreen().catch(() => {});
  } else {
    document.documentElement.requestFullscreen?.().catch(() => {});
  }
}

/** Keeps the display on during a session (a phone that sleeps loses it), and lets it sleep after. */
export function keepAwake(awake) {
  wantAwake = awake;
  applyWake();
}

/** Shows that the page could not start. */
export function failed(code) {
  const loading = document.getElementById('loading');
  loading.dataset.failed = code;
  document.getElementById('failure').textContent = code;
}

/** Takes the screen shown while .NET starts away. */
export function ready() {
  document.getElementById('loading').classList.add('done');
}

/** Answers a read of the service worker with the bytes .NET read. */
export function readDone(request, view) {
  const port = reads.get(request);
  reads.delete(request);
  const bytes = view.slice();
  port?.postMessage({ bytes: bytes.buffer }, [bytes.buffer]);
}

/** Fails a read of the service worker. */
export function readFailed(request) {
  const port = reads.get(request);
  reads.delete(request);
  port?.postMessage({ bytes: null });
}

/** Takes or gives back the wake lock as the page is shown and a session is open. */
function applyWake() {
  const wanted = wantAwake && document.visibilityState === 'visible';
  if (wanted && !wakeLock && navigator.wakeLock) {
    navigator.wakeLock.request('screen').then((lock) => {
      wakeLock = lock;
      lock.addEventListener('release', () => {
        wakeLock = null;
      });
    }).catch(() => {});
  } else if (!wanted && wakeLock) {
    wakeLock.release().catch(() => {});
    wakeLock = null;
  }
}

/** Answers the service worker: the size of the film and its bytes. */
function onWorkerMessage(e) {
  const message = e.data;
  const port = e.ports[0];
  if (!port || !streams) {
    return;
  }
  if (message.type === 'length') {
    port.postMessage({ length: streams.Length(message.stream), type: '' });
  } else if (message.type === 'read') {
    const request = nextRead++;
    reads.set(request, port);
    streams.Read(request, message.stream, message.offset, message.count);
  }
}
