// The strip of camera tiles: this device and every participant, with the name, a frame while speaking, the switched
// off devices and a mark when the cameras found no path. The pictures come from conference.js, the rest from the
// snapshot of .NET; the words come from the language packs.

import * as conference from './conference.js';

const tiles = new Map();
const avatars = new Map();

/** Keeps the picture a participant shows while its camera is off; an empty one resets it. */
export function setAvatar(peer, base64) {
  const old = avatars.get(peer);
  if (old) {
    URL.revokeObjectURL(old);
    avatars.delete(peer);
  }
  if (base64) {
    const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
    avatars.set(peer, URL.createObjectURL(new Blob([bytes], { type: 'image/png' })));
  }
}

/** Draws the tiles of the participants of a snapshot into a container. */
export function render(container, participants, text) {
  const seen = new Set();
  for (const participant of participants) {
    seen.add(participant.peer);
    let tile = tiles.get(participant.peer);
    if (!tile) {
      tile = create(participant.isLocal);
      tiles.set(participant.peer, tile);
    }
    container.append(tile.root);
    draw(tile, participant, text);
  }

  for (const [peer, tile] of tiles) {
    if (!seen.has(peer)) {
      tile.video.srcObject = null;
      tile.root.remove();
      tiles.delete(peer);
    }
  }
}

/** Takes every tile off the page. */
export function clear() {
  for (const tile of tiles.values()) {
    tile.video.srcObject = null;
    tile.root.remove();
  }
  tiles.clear();
}

/** Makes an empty tile; its video never plays sound, the voices go through Web Audio. */
function create(isLocal) {
  const root = document.createElement('figure');
  root.className = 'tile' + (isLocal ? ' local' : '');
  const video = document.createElement('video');
  video.autoplay = true;
  video.playsInline = true;
  video.muted = true;
  const initial = document.createElement('span');
  initial.className = 'initial';
  const avatar = document.createElement('img');
  avatar.className = 'avatar';
  avatar.alt = '';
  const caption = document.createElement('figcaption');
  const name = document.createElement('span');
  name.className = 'name';
  const state = document.createElement('span');
  state.className = 'state';
  caption.append(name, state);
  root.append(video, avatar, initial, caption);
  return { root, video, avatar, initial, name, state, stream: null };
}

/** Fills a tile with the state of a participant. */
function draw(tile, participant, text) {
  const stream = participant.cameraOff ? null : participant.isLocal ? conference.localVideo() : conference.remoteVideo(participant.peer);
  const track = stream?.getVideoTracks()[0] ?? null;
  if ((tile.stream?.getVideoTracks()[0] ?? null) !== track) {
    tile.stream = stream;
    tile.video.srcObject = stream;
    tile.video.play().catch(() => {});
  } else if (tile.stream && tile.video.paused) {
    // Safari leaves a video paused when its track had no frames yet at the first play; it is asked again.
    tile.video.play().catch(() => {});
  }

  tile.root.classList.toggle('speaking', participant.speaking);
  tile.root.classList.toggle('no-picture', !track);
  const picture = avatars.get(participant.peer) ?? '';
  if ((tile.avatar.getAttribute('src') ?? '') !== picture) {
    if (picture) {
      tile.avatar.src = picture;
    } else {
      tile.avatar.removeAttribute('src');
    }
  }
  tile.root.classList.toggle('has-avatar', Boolean(picture));
  tile.name.textContent = participant.name;
  tile.initial.textContent = (participant.name || '?').trim().charAt(0).toUpperCase();

  const marks = [];
  if (!participant.linked) {
    marks.push(text('Browser.Peer.NoConnection'));
  }
  if (participant.microphoneOff) {
    marks.push(text('Main.Participant.MicrophoneOffTip'));
  }
  tile.state.textContent = marks.join(' · ');
}
