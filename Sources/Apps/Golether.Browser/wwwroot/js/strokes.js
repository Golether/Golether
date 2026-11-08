// The pen strokes of the others over the picture, as the applications draw them: the points are fractions of the
// picture of the film (the black bars around it are not part of it); a finished stroke stays for a moment and fades,
// one nobody finishes is dropped after a while. A browser draws nothing itself: the pen is a feature of the application.

const HOLD = 300;
const FADE = 1200;
const ABANDONED = 20000;
const WIDTH = 4;
const PHASE = { begin: 0, more: 1, end: 2 };

const strokes = new Map();
let canvas = null;
let video = null;
let frame = 0;

/** Takes the canvas over the picture and the video it covers. */
export function attach(strokeCanvas, videoElement) {
  canvas = strokeCanvas;
  video = videoElement;
}

/** Takes a part of a stroke of another participant. */
export function add(part) {
  if (part.local || !Array.isArray(part.points) || typeof part.id !== 'string') {
    return;
  }
  const key = part.sender + '/' + part.id;
  let stroke = strokes.get(key);
  if (!stroke || part.phase === PHASE.begin) {
    stroke = { color: colorOf(part.sender), points: [], ended: 0, touched: 0 };
    strokes.set(key, stroke);
  }
  for (let i = 0; i + 1 < part.points.length; i += 2) {
    stroke.points.push([clamp(part.points[i]), clamp(part.points[i + 1])]);
  }
  stroke.touched = performance.now();
  if (part.phase === PHASE.end) {
    stroke.ended = stroke.touched;
  }
  schedule();
}

/** Takes every stroke off the picture. */
export function clear() {
  strokes.clear();
  schedule();
}

/** A fraction of the picture, kept inside it. */
function clamp(value) {
  return Math.min(1, Math.max(0, Number(value) || 0));
}

/** A colour of a participant: the same on every screen of this page. */
function colorOf(peer) {
  let hash = 0;
  for (const c of String(peer)) {
    hash = (hash * 31 + c.charCodeAt(0)) >>> 0;
  }
  return `hsl(${hash % 360}, 85%, 60%)`;
}

/** Asks for a redraw on the next frame. */
function schedule() {
  if (!frame) {
    frame = requestAnimationFrame(draw);
  }
}

/** The part of the element the picture takes: the film is fitted inside with bars around it. */
function pictureBox() {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  const ratio = video?.videoWidth && video?.videoHeight ? video.videoWidth / video.videoHeight : width / height;
  const fitted = width / height > ratio ? { w: height * ratio, h: height } : { w: width, h: width / ratio };
  return { x: (width - fitted.w) / 2, y: (height - fitted.h) / 2, w: fitted.w, h: fitted.h };
}

/** Draws the strokes that are still shown and forgets the others. */
function draw() {
  frame = 0;
  if (!canvas) {
    return;
  }
  const scale = window.devicePixelRatio || 1;
  canvas.width = Math.round(canvas.clientWidth * scale);
  canvas.height = Math.round(canvas.clientHeight * scale);
  const g = canvas.getContext('2d');
  g.setTransform(scale, 0, 0, scale, 0, 0);
  g.clearRect(0, 0, canvas.clientWidth, canvas.clientHeight);
  g.lineWidth = WIDTH;
  g.lineCap = 'round';
  g.lineJoin = 'round';

  const box = pictureBox();
  const now = performance.now();
  for (const [key, stroke] of strokes) {
    const age = stroke.ended ? now - stroke.ended : 0;
    if ((stroke.ended && age >= HOLD + FADE) || (!stroke.ended && now - stroke.touched >= ABANDONED)) {
      strokes.delete(key);
      continue;
    }
    g.globalAlpha = age > HOLD ? 1 - (age - HOLD) / FADE : 1;
    g.strokeStyle = stroke.color;
    g.beginPath();
    stroke.points.forEach(([x, y], i) => {
      const px = box.x + x * box.w;
      const py = box.y + y * box.h;
      if (i === 0) {
        g.moveTo(px, py);
        g.lineTo(px + 0.01, py);
      } else {
        g.lineTo(px, py);
      }
    });
    g.stroke();
  }
  g.globalAlpha = 1;
  if (strokes.size > 0) {
    schedule();
  }
}
