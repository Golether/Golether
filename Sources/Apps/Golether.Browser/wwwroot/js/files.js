// The files the user chose. The browser reads them in place, piece by piece; nothing is uploaded or copied.

const files = new Map();
const urls = new Map();
let next = 1;

/** Remembers a chosen file and gives its number. */
export function add(file) {
  const id = next++;
  files.set(id, file);
  return id;
}

/** Reads a part of a file. */
export async function read(id, offset, count) {
  const file = files.get(id);
  if (!file) {
    throw new Error('NoFile');
  }
  return new Uint8Array(await file.slice(offset, offset + count).arrayBuffer());
}

/** Copies bytes into the memory of .NET. */
export function copy(bytes, view) {
  view.set(bytes);
  return bytes.length;
}

/** The address the <video> element plays a file from. */
export function objectUrl(id) {
  if (!urls.has(id)) {
    const file = files.get(id);
    if (!file) {
      throw new Error('NoFile');
    }
    urls.set(id, URL.createObjectURL(file));
  }
  return urls.get(id);
}

/** Forgets every file. */
export function clear() {
  for (const url of urls.values()) {
    URL.revokeObjectURL(url);
  }
  urls.clear();
  files.clear();
}
