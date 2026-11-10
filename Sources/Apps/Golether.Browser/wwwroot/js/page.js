// The page around the canvas: the servers it was published with, the invitation in its address, the settings of this
// browser, the clipboard, the full screen, the sleep of the display and the bytes of the film the service worker asks
// for. .NET decides; this module only holds the objects of the browser.

const PREFIX_KEYS = /^golether\./;
// The project page the logo of the top bar leads to.
const HOME = 'https://golether.github.io/';
// The seal of the server addresses of the project page (Golether.Security.Discovery.EndpointSeal): AES-256-GCM with
// the SHA-256 of this phrase as the key, the text is the prefix and base64url of nonce, tag and ciphertext. A mask
// against scanners, not a secret: the key is in the open source.
const SEAL_PHRASE = 'Golether server directory, seal 1';
const SEAL_PREFIX = 'g1.';
const SEAL_MAX_LENGTH = 512;
const SEAL_NONCE_BYTES = 12;
const SEAL_TAG_BYTES = 16;

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
  bindSoftKeyboard();
}

/*
  The keyboard of a touch screen. Avalonia takes text only from a key that names a character and from the end of a
  composition; the keyboard of a phone sends neither: its keys are "Unidentified" and it writes into the hidden input
  of Avalonia itself, which Avalonia does not read (the text never arrives). Avalonia does not move the caret of that
  input either when the caret of the field moves, so the keyboard edits the wrong place (Backspace only moves the
  caret). On a touch screen the page therefore keeps the text events of that input from Avalonia, compares the input
  before and after each change and hands .NET the replaced stretch, which it types into the field; .NET in turn keeps
  the text and the caret of the input equal to those of the field (syncSoftKeyboard). A real key (a keyboard plugged
  into a tablet) is still typed by Avalonia, and its echo in the input is left alone.
*/
const AVALONIA_INPUT = 'avalonia-input-element';
const UNTYPED_KEYS = new Set(['Unidentified', 'Process']);
const UNTYPED_KEY_CODE = 229;
const softKeyboard = Boolean(window.matchMedia?.('(pointer: coarse)').matches);
let beforeEdit = null;
let keyTyped = false;

/** Tells whether an event is about the hidden input of Avalonia. */
function isAvaloniaInput(target) {
  return target instanceof HTMLInputElement && target.classList.contains(AVALONIA_INPUT);
}

/** The value and the selection of the hidden input before the keyboard changes it. */
function snapshot(input) {
  return { value: input.value, start: input.selectionStart ?? input.value.length, end: input.selectionEnd ?? input.value.length };
}

/**
 * Makes the hidden input of Avalonia hold the text and the selection of the field being typed in, so the keyboard of a
 * phone edits where the caret of the field is. Nothing changes when they are equal already: touching the input in the
 * middle of a word would make the keyboard start the word anew.
 */
export function syncSoftKeyboard(text, start, end) {
  if (!softKeyboard) {
    return;
  }
  const input = document.querySelector('.' + AVALONIA_INPUT);
  if (!input || document.activeElement !== input) {
    return;
  }
  if (input.value !== text) {
    input.value = text;
  }
  if (input.selectionStart !== start || input.selectionEnd !== end) {
    input.setSelectionRange(start, end);
  }
}

/** Takes the text events of the hidden input of Avalonia over on a touch screen. */
function bindSoftKeyboard() {
  if (!softKeyboard) {
    return;
  }
  window.addEventListener('keydown', (e) => {
    if (isAvaloniaInput(e.target)) {
      keyTyped = !UNTYPED_KEYS.has(e.key) && e.keyCode !== UNTYPED_KEY_CODE;
      // A phone announces each change with an "Unidentified" key; some changes come without the beforeinput event.
      beforeEdit = keyTyped ? null : snapshot(e.target);
    }
  }, { capture: true });
  window.addEventListener('keyup', (e) => {
    if (isAvaloniaInput(e.target)) {
      keyTyped = false;
    }
  }, { capture: true });
  for (const type of ['compositionstart', 'compositionupdate', 'compositionend']) {
    window.addEventListener(type, (e) => {
      if (isAvaloniaInput(e.target)) {
        e.stopImmediatePropagation();
      }
    }, { capture: true });
  }
  window.addEventListener('beforeinput', (e) => {
    if (!isAvaloniaInput(e.target) || keyTyped) {
      return;
    }
    beforeEdit = snapshot(e.target);
    e.stopImmediatePropagation();
  }, { capture: true });
  window.addEventListener('input', (e) => {
    if (!isAvaloniaInput(e.target)) {
      return;
    }
    const before = beforeEdit;
    beforeEdit = null;
    if (keyTyped || !before || !pageCallbacks) {
      return;
    }
    const edit = textEdit(before, e.target.value);
    if (edit) {
      pageCallbacks.OnTextEdit(edit.from, edit.to, edit.text);
    }
  }, { capture: true });
}

/**
 * Finds what changed between two values of the input: the stretch of the old value that was replaced, counted from
 * the start of the old selection, and the text put in its place. The stretch always covers the selection, so the
 * change lands where the caret is even when the same letters stand around it.
 */
export function textEdit(before, after) {
  const old = before.value;
  const shorter = Math.min(old.length, after.length);
  let prefix = 0;
  while (prefix < shorter && prefix < before.start && old[prefix] === after[prefix]) {
    prefix++;
  }
  let suffix = 0;
  while (suffix < shorter - prefix && suffix < old.length - before.end && old[old.length - 1 - suffix] === after[after.length - 1 - suffix]) {
    suffix++;
  }
  const from = prefix - before.start;
  const to = old.length - suffix - before.start;
  const text = after.slice(prefix, after.length - suffix);
  return from === to && text === '' ? null : { from, to, text };
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

/**
 * Opens a sealed address of config.json; the text came from the network, so anything that is not a valid seal gives
 * an empty string, which .NET refuses.
 */
export async function openSeal(text) {
  if (typeof text !== 'string' || text.length > SEAL_MAX_LENGTH || !text.startsWith(SEAL_PREFIX)) {
    return '';
  }
  const encoded = text.slice(SEAL_PREFIX.length);
  if (!/^[A-Za-z0-9_-]+$/.test(encoded)) {
    return '';
  }
  try {
    const packed = Uint8Array.from(atob(encoded.replace(/-/g, '+').replace(/_/g, '/')), (c) => c.charCodeAt(0));
    if (packed.length <= SEAL_NONCE_BYTES + SEAL_TAG_BYTES) {
      return '';
    }
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(SEAL_PHRASE));
    const key = await crypto.subtle.importKey('raw', digest, 'AES-GCM', false, ['decrypt']);
    // WebCrypto wants the tag after the ciphertext; the seal keeps it before.
    const cipher = packed.subarray(SEAL_NONCE_BYTES + SEAL_TAG_BYTES);
    const sealed = new Uint8Array(cipher.length + SEAL_TAG_BYTES);
    sealed.set(cipher);
    sealed.set(packed.subarray(SEAL_NONCE_BYTES, SEAL_NONCE_BYTES + SEAL_TAG_BYTES), cipher.length);
    const iv = packed.subarray(0, SEAL_NONCE_BYTES);
    const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv, tagLength: SEAL_TAG_BYTES * 8 }, key, sealed);
    return new TextDecoder('utf-8', { fatal: true }).decode(plain);
  } catch {
    return '';
  }
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

/** Opens the project page: in a new tab during a session, which leaving the page would end, otherwise in this one. */
export function openHome(newTab) {
  if (newTab) {
    window.open(HOME, '_blank', 'noopener');
  } else {
    location.href = HOME;
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
