// The keyboard of a touch screen.
//
// Avalonia types through a hidden input of the page: it takes text from a key that names a character and from the end
// of a composition, and it copies the text of the field into the input whenever the field changes. That suits a
// physical keyboard and breaks the keyboard of a phone, which sends "Unidentified" keys, writes into the input itself
// while it holds the word being typed, and starts the word anew whenever the input changes under it: letters were
// lost, words doubled on the space, Backspace worked only now and then.
//
// On a touch screen this module therefore owns the input, as other engines that draw into a canvas do. The input is the
// truth of what the keyboard did: after every change the page compares it with the text and the selection the field
// had (the mirror) and has .NET make the same change in the field at once, so the two never differ. Avalonia's copies
// into the input are ignored; .NET writes the text of the field there itself, and only when the field changed on its
// own (another field took the focus, a button filled it). The keys that change text never reach Avalonia; the others
// (Enter, the arrows, Tab, Escape) and the shortcuts with Ctrl or Meta still do.
//
// Adding ?inputlog to the address shows the events of the keyboard on the page, for finding out what a keyboard sends.

const AVALONIA_INPUT = 'avalonia-input-element';
const TEXT_KEYS = new Set(['Backspace', 'Delete', 'Unidentified', 'Process']);
const COMPOSITION_KEY_CODE = 229;
const LOG_LINES = 40;
const touchScreen = Boolean(window.matchMedia?.('(pointer: coarse)').matches);
const valueProperty = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
const setSelectionRange = HTMLInputElement.prototype.setSelectionRange;

let callbacks = null;
let mirror = null;
let avaloniaKey = false;
let logElement = null;

/** Tells whether an event is about the hidden input of Avalonia. */
function isAvaloniaInput(target) {
  return target instanceof HTMLInputElement && target.classList.contains(AVALONIA_INPUT);
}

/** The text and the selection of the input now. */
function stateOf(input) {
  const value = valueProperty.get.call(input);
  return { value, start: input.selectionStart ?? value.length, end: input.selectionEnd ?? value.length };
}

/** Tells whether a key changes text; such a key is typed by the browser into the input and never by Avalonia. */
function isTextKey(e) {
  if (e.ctrlKey || e.metaKey || e.altKey) {
    return false;
  }
  return e.key.length === 1 || TEXT_KEYS.has(e.key) || e.keyCode === COMPOSITION_KEY_CODE;
}

/** Makes the copies Avalonia writes into the input do nothing: on a touch screen the input belongs to this module. */
function own(input) {
  if (input.dataset.goletherOwned) {
    return;
  }
  input.dataset.goletherOwned = 'true';
  Object.defineProperty(input, 'value', {
    configurable: true,
    get() {
      return valueProperty.get.call(this);
    },
    set() {
      // Avalonia's copy of the field; the text of the field comes through sync.
    },
  });
  input.setSelectionRange = () => {};
}

/** Takes the exports of .NET that apply the changes, and starts listening on a touch screen. */
export function bind(softKeyboardCallbacks) {
  callbacks = softKeyboardCallbacks;
  if (!touchScreen) {
    return;
  }
  if (new URLSearchParams(location.search).has('inputlog')) {
    startLog();
  }

  window.addEventListener('focusin', (e) => {
    if (isAvaloniaInput(e.target)) {
      own(e.target);
    }
  }, { capture: true });
  window.addEventListener('keydown', (e) => {
    if (!isAvaloniaInput(e.target)) {
      return;
    }
    log(`key ${e.key} code=${e.code} ${e.keyCode}`);
    if (isTextKey(e)) {
      // The browser changes the input as the key asks; the change is read on input.
      e.stopImmediatePropagation();
    } else {
      avaloniaKey = true;
    }
  }, { capture: true });
  window.addEventListener('keyup', (e) => {
    if (isAvaloniaInput(e.target) && isTextKey(e)) {
      e.stopImmediatePropagation();
    }
  }, { capture: true });
  for (const type of ['compositionstart', 'compositionupdate', 'compositionend', 'beforeinput']) {
    window.addEventListener(type, (e) => {
      if (isAvaloniaInput(e.target)) {
        log(`${type} ${e.inputType ?? ''} ${JSON.stringify(e.data ?? '')}`);
        e.stopImmediatePropagation();
      }
    }, { capture: true });
  }
  window.addEventListener('input', (e) => {
    if (isAvaloniaInput(e.target)) {
      onInput(e.target);
    }
  }, { capture: true });
  document.addEventListener('selectionchange', () => {
    const input = document.activeElement;
    if (isAvaloniaInput(input)) {
      onSelectionChange(input);
    }
  });

  // The page never scrolls: Avalonia fills it and scrolls inside. When the keyboard opens, the browser may scroll the
  // page to show the hidden input, which leaves an empty strip over the field; the page is put back at once, and the
  // field itself is scrolled into sight by .NET.
  window.addEventListener('scroll', keepPagePut, { passive: true });
  window.visualViewport?.addEventListener('resize', keepPagePut);
  window.visualViewport?.addEventListener('scroll', keepPagePut);
}

/** Puts the page back at its top after the browser scrolled it. */
function keepPagePut() {
  const viewport = window.visualViewport;
  log(`viewport ${window.innerWidth}x${window.innerHeight} visual ${Math.round(viewport?.height ?? 0)} at ${Math.round(viewport?.offsetTop ?? 0)} scroll ${window.scrollY}`);
  if (window.scrollX !== 0 || window.scrollY !== 0) {
    window.scrollTo(0, 0);
  }
}

/** Hands .NET the change the keyboard made to the input, so the field makes the same one. */
function onInput(input) {
  const now = stateOf(input);
  log(`input ${JSON.stringify(now.value)} [${now.start},${now.end}]`);
  const before = mirror;
  mirror = now;
  if (avaloniaKey) {
    // A key of a real keyboard: Avalonia typed it into the field itself.
    avaloniaKey = false;
    return;
  }
  if (!before || !callbacks) {
    return;
  }
  const edit = textEdit(before, now.value);
  if (edit) {
    callbacks.OnTextEdit(edit.from + before.start, edit.to + before.start, edit.text, now.start, now.end);
  }
}

/** Moves the caret of the field after the keyboard moved the one of the input (a swipe along the space bar). */
function onSelectionChange(input) {
  if (!mirror || !callbacks) {
    return;
  }
  const now = stateOf(input);
  if (now.value !== mirror.value || (now.start === mirror.start && now.end === mirror.end)) {
    return;
  }
  mirror = now;
  log(`select [${now.start},${now.end}]`);
  callbacks.OnSelect(now.start, now.end);
}

/**
 * Puts the text and the selection of the field into the input when they differ from it: the field took the focus or
 * changed on its own. Equal, the input is left alone, so the keyboard keeps the word it holds.
 */
export function sync(text, start, end) {
  if (!touchScreen) {
    return;
  }
  const input = document.querySelector('.' + AVALONIA_INPUT);
  if (!input || document.activeElement !== input) {
    return;
  }
  own(input);
  const now = stateOf(input);
  if (now.value !== text) {
    valueProperty.set.call(input, text);
  }
  if (now.value !== text || now.start !== start || now.end !== end) {
    setSelectionRange.call(input, start, end);
    log(`sync ${JSON.stringify(text)} [${start},${end}]`);
  }
  mirror = { value: text, start, end };
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

/** Shows the log of the keyboard over the page. */
function startLog() {
  logElement = document.createElement('pre');
  logElement.style.cssText = 'position:fixed;left:0;right:0;top:0;max-height:40vh;overflow:auto;margin:0;padding:4px;'
    + 'background:rgba(0,0,0,0.8);color:#9f9;font:11px monospace;z-index:10;pointer-events:none;white-space:pre-wrap';
  document.body.appendChild(logElement);
}

/** Adds a line to the log of the keyboard, when it is shown. */
function log(line) {
  if (!logElement) {
    return;
  }
  const lines = (logElement.textContent + line + '\n').split('\n');
  logElement.textContent = lines.slice(Math.max(0, lines.length - LOG_LINES - 1)).join('\n');
}
