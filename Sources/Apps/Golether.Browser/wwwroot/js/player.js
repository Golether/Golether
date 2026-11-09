// The film element the sessions control: an <avbridge-video>, which plays MP4 and WebM itself, repackages MKV for the
// browser, and decodes AVI, AC-3, DTS and the rest with libav.js when the browser cannot. The session decides what is
// shown and when; the element only plays. avbridge is fetched with the first film, not with the page.

import * as files from './files.js';
import * as stage from './stage.js';

const AVBRIDGE = '../lib/avbridge/dist/element-browser.js';
const HAVE_CURRENT_DATA = 2;
const HAVE_FUTURE_DATA = 3;
const LOAD_TIMEOUT = 60000;
const LOAD_POLL = 100;
// What a canvas strategy reports as loaded ahead once it has frames and sound to start with: it decodes only what it
// plays, a few frames ahead, so the browser cannot tell; .NET reads it from the cache of the session instead.
const AHEAD_UNKNOWN = -1;
// How far a switch of the sound track may leave a playing film behind or ahead of where it would have been before
// the film is moved there, in seconds: within it the drift correction of the session is quick enough.
const TRACK_SWITCH_SLACK = 0.1;
// The longest a switch of the sound track is waited for, in milliseconds.
const TRACK_SWITCH_TIMEOUT = 10000;
// The longest a switch of the sound track is counted as the cost of the seek that follows it, in seconds.
const TRACK_SWITCH_MAX_COST = 3;
// The strategies that play in the <video> of the browser; the others draw on a canvas and report readiness coarser.
const NATIVE = new Set(['native', 'remux']);

let callbacks = null;
let bridge = null;
let volume = 1;
let muted = false;
// The sound track chosen on this page; avbridge plays the first one until another is chosen.
let selected = null;

/** Takes the exports of .NET that hear about the tracks. */
export function bind(playerCallbacks) {
  callbacks = playerCallbacks;
}

/** Loads avbridge once. */
function loadBridge() {
  bridge ??= import(AVBRIDGE).then(() => customElements.whenDefined('avbridge-video'));
  return bridge;
}

/** The element. */
function film() {
  return stage.film();
}

/** Tells .NET that the tracks of the film changed. */
function tracksChanged() {
  callbacks?.TracksChanged();
}

/** Loads a source and resolves when its length is known; fails with 'Format' when it cannot be played here. */
export async function load(source, start, paused) {
  await loadBridge();
  const v = film();
  if (!v.dataset.wired) {
    v.dataset.wired = '1';
    for (const name of ['loadedmetadata', 'tracks', 'strategychange', 'ready']) {
      v.addEventListener(name, tracksChanged);
    }
  }

  // The length is known once avbridge has read the file; the repackaging strategy fires loadedmetadata only after
  // the first play, so the length itself is watched as well.
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => done(false), LOAD_TIMEOUT);
    const poll = setInterval(() => {
      if (v.player && v.duration > 0) {
        done(true);
      }
    }, LOAD_POLL);
    const onLoaded = () => done(true);
    const onError = () => done(false);
    function done(ok) {
      clearTimeout(timer);
      clearInterval(poll);
      v.removeEventListener('loadedmetadata', onLoaded);
      v.removeEventListener('error', onError);
      if (ok) {
        resolve();
      } else {
        reject(new Error('Format'));
      }
    }
    v.addEventListener('loadedmetadata', onLoaded);
    v.addEventListener('error', onError);
    selected = null;
    if (source.startsWith('file:')) {
      v.source = files.get(Number(source.slice('file:'.length)));
    } else {
      v.src = new URL(source, location.href).href;
    }
  });

  v.volume = volume;
  v.muted = muted;
  v.currentTime = start;
  if (!paused) {
    play(v);
  }
  tracksChanged();
}

/** Plays; a browser that wants a click first is asked again with the next click on the page. */
function play(v) {
  v.play().catch((e) => {
    if (e?.name === 'NotAllowedError') {
      document.addEventListener('pointerdown', () => v.play().catch(() => {}), { once: true, capture: true });
    }
  });
}

/** Pauses or plays. */
export function setPaused(paused) {
  const v = film();
  if (paused) {
    v.pause();
  } else if (v.paused) {
    play(v);
  }
}

/** Moves to a position in seconds. */
export function seek(seconds) {
  film().currentTime = seconds;
}

/** Changes the rate; the pitch stays. */
export function setRate(rate) {
  film().playbackRate = rate;
}

/** Unloads the source. */
export function stop() {
  const v = film();
  if (bridge) {
    v.pause?.();
    v.src = null;
  }
  tracksChanged();
}

/** Sets the volume of this page, from 0 to 1. */
export function setVolume(fraction) {
  volume = fraction;
  if (bridge) {
    film().volume = fraction;
  }
}

/** Mutes or unmutes the film on this page. */
export function setMuted(value) {
  muted = value;
  if (bridge) {
    film().muted = value;
  }
}

/** The sound tracks of the film as JSON: id, language, title, codec, channels and whether it plays. */
export function audioTracks() {
  const v = film();
  const tracks = bridge && Array.isArray(v.audioTracks) ? v.audioTracks : [];
  return JSON.stringify(tracks.map((t, i) => ({
    id: t.id,
    language: t.language ?? '',
    title: t.label ?? t.title ?? '',
    codec: t.codec ?? '',
    channels: t.channels ?? null,
    selected: selected == null ? i === 0 : t.id === selected,
  })));
}

/**
 * Plays one sound track of the film, on this page only. avbridge switches by decoding again from the last keyframe,
 * and the picture stands still meanwhile; a playing film then moves on to where it would have been, so the page does
 * not lag behind the others while the drift correction of the session slowly catches up.
 */
export async function selectAudioTrack(id) {
  const v = film();
  const playing = !v.paused;
  const from = v.currentTime;
  const rate = v.playbackRate || 1;
  const started = performance.now();

  selected = id;
  await v.setAudioTrack(id);
  if (playing) {
    await playingAgain(v, started);
    catchUp(v, from, rate, started);
  }
  tracksChanged();
}

/** Resolves when the element plays again after a switch, or when waiting is pointless. */
async function playingAgain(v, started) {
  const ready = NATIVE.has(v.strategy) ? HAVE_FUTURE_DATA : HAVE_CURRENT_DATA;
  while (performance.now() - started < TRACK_SWITCH_TIMEOUT && !v.paused && (v.readyState < ready || v.seeking)) {
    await new Promise((resolve) => setTimeout(resolve, LOAD_POLL));
  }
}

/**
 * Moves a film that a switch held up to where it would have been without the switch. The move costs about as long
 * as the switch did (the same decoding from the keyframe), so it aims that much further.
 */
function catchUp(v, from, rate, started) {
  if (v.paused) {
    return;
  }
  const took = (performance.now() - started) / 1000;
  const expected = from + took * rate;
  if (Math.abs(v.currentTime - expected) <= TRACK_SWITCH_SLACK) {
    return;
  }
  const target = expected + Math.min(took, TRACK_SWITCH_MAX_COST) * rate;
  v.currentTime = Number.isFinite(v.duration) ? Math.min(target, v.duration) : target;
}

/** The width of the picture divided by its height, or 0 before it is known. */
export function aspect() {
  const v = film();
  return v.videoWidth > 0 && v.videoHeight > 0 ? v.videoWidth / v.videoHeight : 0;
}

/** The state of the element: loaded, position, duration, paused, buffering, seconds loaded ahead (-1: ask .NET), rate. */
export function snapshot() {
  const v = film();
  if (!bridge || !v.player) {
    return [0, NaN, NaN, 1, 0, 0, 1];
  }
  const loaded = v.readyState >= 1 || v.duration > 0 ? 1 : 0;
  let ahead = 0;
  const buffered = v.buffered;
  for (let i = 0; i < (buffered?.length ?? 0); i++) {
    if (buffered.start(i) <= v.currentTime + 0.25 && buffered.end(i) >= v.currentTime) {
      ahead = buffered.end(i) - v.currentTime;
    }
  }
  const native = NATIVE.has(v.strategy);
  const ready = native ? HAVE_FUTURE_DATA : HAVE_CURRENT_DATA;
  if (!native) {
    ahead = v.readyState >= HAVE_CURRENT_DATA ? AHEAD_UNKNOWN : 0;
  }
  const buffering = !v.paused && v.readyState < ready ? 1 : 0;
  return [loaded, v.currentTime, Number.isFinite(v.duration) ? v.duration : NaN, v.paused ? 1 : 0, buffering, ahead, v.playbackRate];
}
