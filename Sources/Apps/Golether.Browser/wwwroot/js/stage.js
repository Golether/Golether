// The elements of the page shown with the canvas of Avalonia: the film element, which Avalonia places and this module
// lays under the canvas, and the cameras, whose current pictures are copied into the tiles Avalonia draws.

import * as conference from './conference.js';

let filmElement = null;
const cameras = new Map();
let canvas = null;
let context = null;

/** The film element, made on the first call; avbridge turns it into a player when the first film is loaded. */
export function film() {
  if (!filmElement) {
    filmElement = document.createElement('avbridge-video');
    filmElement.setAttribute('playsinline', '');
    filmElement.setAttribute('fit', 'contain');
    filmElement.style.width = '100%';
    filmElement.style.height = '100%';
    filmElement.style.display = 'block';
    filmElement.style.background = '#000';
  }
  return filmElement;
}

/**
 * Lays the layer Avalonia keeps the elements of the page in under its canvas. The canvas is transparent wherever no
 * control paints, so the film shows through and the controls are drawn over it; the clicks reach the canvas.
 */
export function sendBack(element) {
  const layer = element.parentElement;
  if (!layer) {
    return;
  }
  layer.style.zIndex = '-1';
  const host = layer.parentElement;
  if (host) {
    // The layer goes behind the canvas but stays in front of the page: the host makes a stacking context of its own.
    if (getComputedStyle(host).position === 'static') {
      host.style.position = 'relative';
    }
    host.style.zIndex = '0';
  }
}

/** Copies the current picture of a camera, scaled into a box, into the memory of .NET as RGBA; gives w * 65536 + h. */
export function cameraFrame(peer, maxWidth, maxHeight, target) {
  const video = cameraElement(peer);
  if (!video || video.readyState < 2 || !video.videoWidth || !video.videoHeight) {
    return 0;
  }

  const scale = Math.min(maxWidth / video.videoWidth, maxHeight / video.videoHeight, 1);
  const width = Math.max(1, Math.floor(video.videoWidth * scale));
  const height = Math.max(1, Math.floor(video.videoHeight * scale));
  if (!canvas) {
    canvas = document.createElement('canvas');
    context = canvas.getContext('2d', { willReadFrequently: true });
  }
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }

  context.drawImage(video, 0, 0, width, height);
  // The memory of .NET takes exactly a Uint8Array; the pixels of a canvas are a Uint8ClampedArray over the same bytes.
  const pixels = context.getImageData(0, 0, width, height).data;
  target.set(new Uint8Array(pixels.buffer, pixels.byteOffset, pixels.length));
  return width * 65536 + height;
}

/** Stops the elements of the cameras of participants that left; this device's own camera stays. */
export function keepCameras(peers) {
  const kept = new Set(peers);
  for (const [peer, entry] of cameras) {
    if (peer && !kept.has(peer)) {
      entry.video.srcObject = null;
      cameras.delete(peer);
    }
  }
}

/** The element that plays a camera, following the track the conference has for it now. */
function cameraElement(peer) {
  const stream = peer ? conference.remoteVideo(peer) : conference.localVideo();
  const track = stream?.getVideoTracks()[0] ?? null;
  let entry = cameras.get(peer);
  if (!track) {
    if (entry) {
      entry.video.srcObject = null;
      entry.track = null;
    }
    return null;
  }

  if (!entry) {
    const video = document.createElement('video');
    video.muted = true;
    video.playsInline = true;
    video.autoplay = true;
    entry = { video, track: null };
    cameras.set(peer, entry);
  }
  if (entry.track !== track) {
    entry.track = track;
    entry.video.srcObject = new MediaStream([track]);
    entry.video.play().catch(() => {});
  } else if (entry.video.paused) {
    entry.video.play().catch(() => {});
  }
  return entry.video;
}
