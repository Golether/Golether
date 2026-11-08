// The <video> element the sessions control. The session decides what is shown and when; the element only plays.

const HAVE_FUTURE_DATA = 3;

/** The element. */
function video() {
  return document.getElementById('video');
}

/** Loads a source and resolves when its length is known; fails with 'Format' when the browser cannot play it. */
export function load(url, start, paused) {
  return new Promise((resolve, reject) => {
    const v = video();
    const done = (ok) => {
      v.removeEventListener('loadedmetadata', onLoaded);
      v.removeEventListener('error', onError);
      if (ok) {
        resolve();
      } else {
        reject(new Error('Format'));
      }
    };
    const onLoaded = () => {
      v.currentTime = start;
      if (!paused) {
        play(v);
      }
      done(true);
    };
    const onError = () => done(false);
    v.addEventListener('loadedmetadata', onLoaded);
    v.addEventListener('error', onError);
    v.preservesPitch = true;
    v.src = url;
    v.load();
  });
}

/** Plays; a browser that wants a click first gets a button for it. */
function play(v) {
  v.play().catch((e) => {
    if (e.name === 'NotAllowedError') {
      document.dispatchEvent(new CustomEvent('golether-autoplay-blocked'));
    }
  });
}

/** Pauses or plays. */
export function setPaused(paused) {
  const v = video();
  if (paused) {
    v.pause();
  } else if (v.paused) {
    play(v);
  }
}

/** Moves to a position in seconds. */
export function seek(seconds) {
  video().currentTime = seconds;
}

/** Changes the rate; the pitch stays. */
export function setRate(rate) {
  video().playbackRate = rate;
}

/** Unloads the source. */
export function stop() {
  const v = video();
  v.pause();
  v.removeAttribute('src');
  v.load();
}

/** The sound tracks of the film where the browser shows them (Safari), or null. */
export function audioTracks() {
  const tracks = video().audioTracks;
  if (!tracks) {
    return null;
  }
  return Array.from(tracks, (t, i) => ({ index: i, label: t.label || t.language || String(i + 1), enabled: t.enabled }));
}

/** Plays one sound track of the film, for this page only. */
export function selectAudioTrack(index) {
  const tracks = video().audioTracks;
  if (tracks) {
    Array.from(tracks).forEach((t, i) => {
      t.enabled = i === index;
    });
  }
}

/** The state of the element: loaded, position, duration, paused, buffering, seconds loaded ahead, rate. */
export function snapshot() {
  const v = video();
  const loaded = v.currentSrc && v.readyState >= 1 ? 1 : 0;
  let ahead = 0;
  for (let i = 0; i < v.buffered.length; i++) {
    if (v.buffered.start(i) <= v.currentTime + 0.25 && v.buffered.end(i) >= v.currentTime) {
      ahead = v.buffered.end(i) - v.currentTime;
    }
  }
  const buffering = !v.paused && (v.seeking || v.readyState < HAVE_FUTURE_DATA) ? 1 : 0;
  return [loaded, v.currentTime, Number.isFinite(v.duration) ? v.duration : NaN, v.paused ? 1 : 0, buffering, ahead, v.playbackRate];
}
