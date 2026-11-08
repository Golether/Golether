// The device key of the browser: ECDSA P-256 of WebCrypto. The private key is made non-extractable, so no script can
// read it out, and is kept in IndexedDB of this site only. The identifier is SHA-256 of the SubjectPublicKeyInfo in
// lowercase hex, the same PeerId as in the desktop application.

const DB_NAME = 'golether';
const STORE = 'keys';
const RECORD = 'device';
const ALGORITHM = { name: 'ECDSA', namedCurve: 'P-256' };
const SIGNING = { name: 'ECDSA', hash: 'SHA-256' };

let device = null;

/** Opens the database of the site; resolves to null when the browser keeps none (a private window). */
function openDb() {
  return new Promise((resolve) => {
    try {
      const request = indexedDB.open(DB_NAME, 1);
      request.onupgradeneeded = () => request.result.createObjectStore(STORE);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => resolve(null);
    } catch {
      resolve(null);
    }
  });
}

/** Runs one request on the store of the keys. */
function onStore(db, mode, action) {
  return new Promise((resolve, reject) => {
    const request = action(db.transaction(STORE, mode).objectStore(STORE));
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

/** Encodes bytes in base64. */
export function toBase64(bytes) {
  let text = '';
  for (const b of bytes) {
    text += String.fromCharCode(b);
  }
  return btoa(text);
}

/** Decodes base64; throws on a malformed text. */
export function fromBase64(text) {
  const raw = atob(text);
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) {
    bytes[i] = raw.charCodeAt(i);
  }
  return bytes;
}

/** The lowercase hex SHA-256 of bytes. */
async function sha256Hex(bytes) {
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(hash, (b) => b.toString(16).padStart(2, '0')).join('');
}

/** Opens the key of the device, making it on the first visit. */
async function open() {
  if (device) {
    return device;
  }

  const db = await openDb();
  let stored = db ? await onStore(db, 'readonly', (s) => s.get(RECORD)).catch(() => null) : null;
  if (!stored) {
    const pair = await crypto.subtle.generateKey(ALGORITHM, false, ['sign', 'verify']);
    const spki = new Uint8Array(await crypto.subtle.exportKey('spki', pair.publicKey));
    stored = { privateKey: pair.privateKey, spki };
    if (db) {
      await onStore(db, 'readwrite', (s) => s.put(stored, RECORD)).catch(() => null);
    }
  }

  device = { privateKey: stored.privateKey, spki: new Uint8Array(stored.spki), peerId: await sha256Hex(stored.spki) };
  return device;
}

/** The identifier of the device. */
export async function peerId() {
  return (await open()).peerId;
}

/** The public key of the device: DER SubjectPublicKeyInfo in base64. */
export async function publicKey() {
  return toBase64((await open()).spki);
}

/** Signs a text with the key of the device; the signature is r‖s (IEEE P1363) in base64. */
export async function sign(text) {
  const key = (await open()).privateKey;
  return toBase64(new Uint8Array(await crypto.subtle.sign(SIGNING, key, new TextEncoder().encode(text))));
}

/**
 * Checks the signature of another device and gives its identifier, or null when the key is not P-256 or the
 * signature does not hold.
 */
export async function verify(publicKeyBase64, text, signatureBase64) {
  try {
    const spki = fromBase64(publicKeyBase64);
    if (spki.length > 256) {
      return null;
    }
    const key = await crypto.subtle.importKey('spki', spki, ALGORITHM, false, ['verify']);
    const valid = await crypto.subtle.verify(SIGNING, key, fromBase64(signatureBase64), new TextEncoder().encode(text));
    return valid ? await sha256Hex(spki) : null;
  } catch {
    return null;
  }
}
