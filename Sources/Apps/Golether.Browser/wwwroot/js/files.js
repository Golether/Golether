// The files the user chose. The browser reads them in place, piece by piece; nothing is uploaded or copied.

const files = new Map();
let next = 1;

/**
 * Asks the user for a file in the dialog of the browser; gives its number, or 0 when the user chose none. The input
 * stays in the page while the dialog is open: a detached one may be collected before the choice arrives.
 */
export function pick() {
  return new Promise((resolve) => {
    const input = document.createElement('input');
    input.type = 'file';
    input.hidden = true;
    const done = (id) => {
      input.remove();
      resolve(id);
    };
    input.accept = 'video/*,audio/*,.mkv,.avi,.wmv,.flv,.ts,.m2ts,.mts,.rm,.rmvb,.3gp,.mov,.m4v,.webm,.ogv';
    input.addEventListener('change', () => {
      const file = input.files?.[0];
      if (!file) {
        done(0);
        return;
      }
      const id = next++;
      files.set(id, file);
      done(id);
    }, { once: true });
    input.addEventListener('cancel', () => done(0), { once: true });
    document.body.append(input);
    input.click();
  });
}

/** The chosen file of a number. */
export function get(id) {
  const file = files.get(id);
  if (!file) {
    throw new Error('NoFile');
  }
  return file;
}

/** The name of a chosen file. */
export function name(id) {
  return get(id).name;
}

/** The size of a chosen file in bytes. */
export function size(id) {
  return get(id).size;
}

/** Reads a part of a file. */
export async function read(id, offset, count) {
  const file = get(id);
  return new Uint8Array(await file.slice(offset, offset + count).arrayBuffer());
}

/** Copies bytes into the memory of .NET. */
export function copy(bytes, view) {
  view.set(bytes);
  return bytes.length;
}

/** Forgets every file. */
export function clear() {
  files.clear();
}
