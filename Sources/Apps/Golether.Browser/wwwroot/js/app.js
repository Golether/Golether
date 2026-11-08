// The page of Golether in a browser: the screens, the controls, the camera tiles and the bridge between the service
// worker and .NET.
// Every word comes from the language packs through .NET (data-t attributes and api.Text); the page has none.

import * as conference from './conference.js';
import * as files from './files.js';
import * as player from './player.js';
import * as strokes from './strokes.js';
import * as tiles from './tiles.js';

const REQUEST = { play: 0, pause: 1, seek: 2, playNow: 3 };
const NAME_KEY = 'golether.name';
const LANGUAGE_KEY = 'golether.language';
const MAX_EVENTS = 50;
const MAX_CHAT = 100;
const CHAT = { text: 0, reaction: 1 };
const TYPES = { mp4: 'video/mp4', m4v: 'video/mp4', mov: 'video/quicktime', webm: 'video/webm', mkv: 'video/x-matroska', ogv: 'video/ogg' };

let api = null;
let streams = null;
let invitePayload = null;
let chosenFile = null;
let snapshot = null;
let redrawQueued = false;
let pendingAsk = null;
let wakeLock = null;
const reads = new Map();
let nextRead = 1;

const $ = (id) => document.getElementById(id);

/** The functions .NET calls on the page. */
export const page = {
  changed() {
    if (!redrawQueued) {
      redrawQueued = true;
      requestAnimationFrame(() => {
        redrawQueued = false;
        render();
      });
    }
  },
  event(text) {
    const item = document.createElement('li');
    const time = document.createElement('time');
    time.textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    item.append(time, ' ', text);
    $('events').prepend(item);
    while ($('events').children.length > MAX_EVENTS) {
      $('events').lastElementChild.remove();
    }
  },
  ask(id, name, code) {
    pendingAsk = id;
    $('admission-title').textContent = api.Format('Admission.Title', [name]);
    $('admission-code').textContent = code;
    $('admission').showModal();
  },
  cancelAsk(id) {
    if (pendingAsk === id) {
      pendingAsk = null;
      $('admission').close();
    }
  },
  chat(json) {
    const message = JSON.parse(json);
    if (message.kind === 1) {
      floatReaction(message.text);
      return;
    }
    const item = document.createElement('li');
    const time = document.createElement('time');
    time.textContent = message.time;
    const who = document.createElement('span');
    who.className = 'who';
    who.textContent = message.name;
    item.append(time, who, message.text);
    $('chat').append(item);
    while ($('chat').children.length > MAX_CHAT) {
      $('chat').firstElementChild.remove();
    }
    $('chat').scrollTop = $('chat').scrollHeight;
  },
  stroke(json) {
    strokes.add(JSON.parse(json));
  },
  avatar(peer, image) {
    if (peer) {
      tiles.setAvatar(peer, image);
      page.changed();
    }
  },
  readDone(request, view) {
    const port = reads.get(request);
    reads.delete(request);
    const bytes = view.slice();
    port?.postMessage({ bytes: bytes.buffer }, [bytes.buffer]);
  },
  readFailed(request) {
    const port = reads.get(request);
    reads.delete(request);
    port?.postMessage({ bytes: null });
  },
};

/** Starts the page once .NET is running. */
export async function start(exports, config) {
  api = exports.Golether.Browser.Interop.BrowserApi;
  streams = exports.Golether.Browser.Interop.StreamCallbacks;

  const stored = localStorage.getItem(LANGUAGE_KEY);
  const language = await api.StartAsync(stored || navigator.language || 'en', config.signal, config.stun, config.meet);
  fillLanguages(language);
  translate();

  navigator.serviceWorker?.addEventListener('message', onWorkerMessage);
  conference.onChange(page.changed);
  document.addEventListener('click', conference.unlock);
  document.addEventListener('visibilitychange', keepAwake);
  wireHome();
  wireSession();
  readInvitation();
  window.addEventListener('hashchange', readInvitation);
  show('home');
}

/** Shows one screen; the screen of a session keeps the display on. */
function show(screen) {
  for (const id of ['loading', 'home', 'session']) {
    $(id).hidden = id !== screen;
  }
  keepAwake();
}

/** Keeps the display on while a session is shown (a phone that sleeps loses the session), and lets it sleep after. */
function keepAwake() {
  const wanted = !$('session').hidden && document.visibilityState === 'visible';
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

/** Stops the cameras and takes the tiles off the page once a session is over. */
function endConference() {
  conference.stopAll();
  tiles.clear();
}

/** Puts the texts of the language into the page. */
function translate() {
  document.documentElement.lang = $('language').value;
  for (const element of document.querySelectorAll('[data-t]')) {
    element.textContent = api.Text(element.dataset.t);
  }
  for (const element of document.querySelectorAll('[data-t-title]')) {
    element.title = api.Text(element.dataset.tTitle);
    element.setAttribute('aria-label', element.title);
  }
  for (const element of document.querySelectorAll('[data-t-placeholder]')) {
    element.placeholder = api.Text(element.dataset.tPlaceholder);
  }
  translateLocked();
  drawTracks();
  render();
}

/** Fills the list of languages. */
function fillLanguages(current) {
  const select = $('language');
  for (const entry of api.Languages()) {
    const [code, name] = entry.split('=');
    select.add(new Option(name, code, false, code === current));
  }
  select.addEventListener('change', () => {
    localStorage.setItem(LANGUAGE_KEY, api.SetLanguage(select.value));
    translate();
    if (invitePayload) {
      describeInvitation();
    }
  });
}

/** Wires the first screen. */
function wireHome() {
  $('name').value = localStorage.getItem(NAME_KEY) || '';
  $('name').addEventListener('change', () => localStorage.setItem(NAME_KEY, $('name').value.trim()));
  $('session-name').placeholder = api.Text('Main.DefaultSessionName');

  $('choose').addEventListener('click', () => $('file').click());
  $('file').addEventListener('change', () => {
    chosenFile = $('file').files[0] ?? null;
    $('file-name').textContent = chosenFile ? chosenFile.name : '';
    $('host').disabled = !chosenFile;
  });

  $('host').addEventListener('click', () => {
    conference.prepare(true, true);
  });
  $('host').addEventListener('click', () => busy($('host'), async () => {
    const id = files.add(chosenFile);
    show('session');
    $('invite-box').hidden = false;
    await api.HostAsync($('session-name').value.trim() || api.Text('Main.DefaultSessionName'), myName(), id, chosenFile.name, chosenFile.size);
    showInvite(false);
  }));

  $('join').addEventListener('click', () => {
    conference.prepare(true, true);
  });
  $('join').addEventListener('click', () => busy($('join'), async () => {
    show('session');
    $('invite-box').hidden = true;
    const promise = api.JoinAsync(invitePayload, myName());
    render();
    await promise;
  }));
}

/** Wires the session screen. */
function wireSession() {
  const video = $('video');

  $('play').addEventListener('click', () => {
    const playing = snapshot?.playback?.playing;
    api.RequestAsync(playing ? REQUEST.pause : REQUEST.play, -1).catch(report);
  });
  $('start-now').addEventListener('click', () => api.RequestAsync(REQUEST.playNow, -1).catch(report));
  $('microphone').addEventListener('click', () => switchDevice(!snapshot?.microphoneOn, snapshot?.cameraOn));
  $('camera').addEventListener('click', () => switchDevice(snapshot?.microphoneOn, !snapshot?.cameraOn));
  $('timeline').addEventListener('change', () => {
    if (Number.isFinite(video.duration)) {
      api.RequestAsync(REQUEST.seek, (Number($('timeline').value) / 1000) * video.duration).catch(report);
    }
  });
  $('fullscreen').addEventListener('click', () => {
    if (document.fullscreenElement) {
      document.exitFullscreen();
    } else {
      $('stage').requestFullscreen?.();
    }
  });
  $('unblock').addEventListener('click', () => {
    $('unblock').hidden = true;
    video.play().catch(() => {});
  });
  document.addEventListener('golether-autoplay-blocked', () => {
    $('unblock').hidden = false;
  });

  $('copy').addEventListener('click', async () => {
    await navigator.clipboard?.writeText($('invite-link').value).catch(() => {});
    $('invite-link').select();
    $('invite-note').textContent = api.Text('Invite.Dialog.Copied');
  });
  $('renew').addEventListener('click', () => showInvite(true));

  wireChat();
  wireLocked();
  strokes.attach($('strokes'), video);
  video.addEventListener('loadedmetadata', drawTracks);
  $('audio-track').addEventListener('change', () => player.selectAudioTrack(Number($('audio-track').value)));

  $('approve').addEventListener('click', () => answer(true));
  $('reject').addEventListener('click', () => answer(false));
  $('admission').addEventListener('cancel', (e) => e.preventDefault());

  $('leave').addEventListener('click', async () => {
    if (snapshot?.isHost && snapshot.state !== 'Ended' && !confirm(api.Text('Mobile.Leave.Host.Text'))) {
      return;
    }
    await api.LeaveAsync().catch(() => {});
    endConference();
    files.clear();
    snapshot = null;
    $('chat').replaceChildren();
    strokes.clear();
    $('events').replaceChildren();
    show('home');
  });

  video.addEventListener('timeupdate', drawTime);
  video.addEventListener('durationchange', drawTime);
  setInterval(render, 1000);
}

/** Switches the devices of this page; a device switched on is asked for in the click itself, as a phone wants. */
function switchDevice(microphone, camera) {
  conference.prepare(Boolean(camera) && !snapshot?.cameraOn, Boolean(microphone) && !snapshot?.microphoneOn);
  api.SetDevices(Boolean(microphone), Boolean(camera));
}

/** Wires the chat and the reactions. */
function wireChat() {
  $('chat-form').addEventListener('submit', (e) => {
    e.preventDefault();
    const text = $('chat-input').value.trim();
    if (!text) {
      return;
    }
    $('chat-input').value = '';
    api.SendChatAsync(CHAT.text, text).catch(report);
  });
  for (const reaction of api.Reactions()) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'btn';
    button.textContent = reaction;
    button.addEventListener('click', () => api.SendChatAsync(CHAT.reaction, reaction).catch(report));
    $('reactions').append(button);
  }
}

/** The features of the application stay on the page, locked: a click tells where to find them. */
function wireLocked() {
  for (const button of document.querySelectorAll('[data-locked]')) {
    button.addEventListener('click', () => page.event(api.Text('Browser.AppOnly')));
  }
}

/** Shows the titles of the locked features in the language of the page. */
function translateLocked() {
  for (const button of document.querySelectorAll('[data-locked]')) {
    button.title = api.Text(button.dataset.locked) + ' — ' + api.Text('Browser.AppOnly');
    button.setAttribute('aria-label', button.title);
    button.setAttribute('aria-disabled', 'true');
  }
}

/** Fills the choice of the sound track, or locks it where the browser cannot switch tracks. */
function drawTracks() {
  const select = $('audio-track');
  const tracks = player.audioTracks();
  select.replaceChildren();
  if (!tracks || tracks.length < 2) {
    select.disabled = true;
    select.title = api.Text(tracks ? 'Main.Tracks.Audio' : 'Browser.Tracks.Unsupported');
    select.add(new Option(api.Text('Main.Tracks.Name'), '-1'));
    return;
  }
  select.disabled = false;
  select.title = api.Text('Main.Tracks.Audio');
  for (const track of tracks) {
    select.add(new Option(track.label, String(track.index), false, track.enabled));
  }
}

/** Lets a reaction rise over the picture for a moment. */
function floatReaction(text) {
  const span = document.createElement('span');
  span.textContent = text;
  span.style.left = 10 + Math.random() * 80 + '%';
  $('floating').append(span);
  setTimeout(() => span.remove(), 3000);
}

/** Answers the question about a guest. */
function answer(approve) {
  if (pendingAsk !== null) {
    api.Answer(pendingAsk, approve);
    pendingAsk = null;
  }
  $('admission').close();
}

/** Shows the invitation of the hosted session. */
function showInvite(renew) {
  const payload = api.Invite(renew);
  $('invite-link').value = location.origin + location.pathname + '#' + payload;
  $('invite-note').textContent = api.Text('Browser.Invite.Note');
}

/** Reads an invitation from the address and takes it out of the address bar at once. */
function readInvitation() {
  const hash = location.hash.slice(1);
  if (!hash) {
    return;
  }
  invitePayload = hash;
  history.replaceState(null, '', location.pathname);
  describeInvitation();
}

/** Shows whose session the invitation leads to. */
function describeInvitation() {
  try {
    const info = JSON.parse(api.Describe(invitePayload));
    $('join-text').textContent = info.expired ? api.Text('Session.Error.InviteExpired') : info.blocked ?? info.text;
    $('join').disabled = info.expired || Boolean(info.blocked);
    $('join-card').hidden = false;
  } catch (e) {
    $('join-card').hidden = true;
    showError(api.Text('Main.Link.Bad.Title') + ': ' + message(e));
  }
}

/** The name of the user. */
function myName() {
  const name = $('name').value.trim();
  localStorage.setItem(NAME_KEY, name);
  return name;
}

/** Runs an action of a button: the button waits, a failure is shown on the first screen. */
async function busy(button, action) {
  button.disabled = true;
  showError('');
  try {
    await action();
  } catch (e) {
    await api.LeaveAsync().catch(() => {});
    endConference();
    show('home');
    showError(message(e));
  } finally {
    button.disabled = false;
    render();
  }
}

/** Shows a failure of an intention in the event feed. */
function report(e) {
  page.event(api.Text('Main.Error.CommandNotSent') + ': ' + message(e));
}

/** The reason of a failure as .NET worded it. */
function message(e) {
  return String(e?.message ?? e).replace(/^Error:\s*/, '');
}

/** Shows or hides the failure on the first screen. */
function showError(text) {
  $('home-error').textContent = text;
  $('home-error').hidden = !text;
}

/** Redraws the session screen from the snapshot of .NET. */
function render() {
  if (!api) {
    return;
  }
  const text = api.Snapshot();
  snapshot = text ? JSON.parse(text) : null;
  if (!snapshot) {
    return;
  }

  $('status').textContent = snapshot.status;
  $('placeholder').textContent = snapshot.media ? '' : snapshot.status;
  $('placeholder').hidden = Boolean(snapshot.media);
  $('approval').hidden = snapshot.state !== 'AwaitingApproval' && snapshot.state !== 'Connecting';
  $('my-code').textContent = snapshot.code ?? '';
  $('play').textContent = snapshot.playback?.playing ? '❚❚' : '▶';
  $('play').title = api.Text(snapshot.playback?.playing ? 'Main.Play.Pause' : 'Main.Play.Play');
  $('play').disabled = snapshot.state !== 'Active' || !snapshot.media;
  $('timeline').disabled = $('play').disabled;
  $('start-now').hidden = !(snapshot.isHost && snapshot.playback?.cause === 'WaitingForParticipants');
  $('leave').textContent = api.Text(snapshot.isHost ? 'Main.Leave' : 'Mobile.Leave.Confirm');
  drawDevices();
  $('keep-open').hidden = !(snapshot.isHost && matchMedia('(pointer: coarse)').matches);
  tiles.render($('tiles'), snapshot.participants, api.Text);

  const header = api.Format('Main.Participants.Header', [String(snapshot.participants.length)]);
  $('participants-header').textContent = header;
  $('participants').replaceChildren(...snapshot.participants.map(drawParticipant));
  drawTime();
}

/** Draws the buttons of the microphone and the camera of this page. */
function drawDevices() {
  const active = snapshot.state === 'Active';
  for (const [id, on, key] of [['microphone', snapshot.microphoneOn, 'Microphone'], ['camera', snapshot.cameraOn, 'Camera']]) {
    const button = $(id);
    button.disabled = !active;
    button.classList.toggle('off', !on);
    button.title = api.Text(`Main.${key}.${on ? 'On' : 'Off'}`);
    button.setAttribute('aria-pressed', String(Boolean(on)));
  }
}

/** One line of the list of participants. */
function drawParticipant(participant) {
  const item = document.createElement('li');
  const name = document.createElement('span');
  name.className = 'name';
  name.textContent = participant.name;
  item.append(name);
  if (participant.isHost) {
    item.append(badge(api.Text('Browser.Badge.Host')));
  }
  if (participant.isLocal) {
    item.append(badge(api.Text('Browser.Badge.You')));
  }
  if (typeof participant.ping === 'number') {
    item.append(badge(participant.ping + ' ms', 'muted'));
  }
  if (participant.buffering) {
    item.append(badge(api.Text('Main.Participant.Loading'), 'muted'));
  }
  if (snapshot.isHost && !participant.isLocal) {
    item.append(moderation(participant, 'microphone', 'Main.Moderation.Microphone', participant.microphoneOff));
    item.append(moderation(participant, 'camera', 'Main.Moderation.Camera', participant.cameraOff));
  }
  return item;
}

/** A button of the host that switches a device of a guest off; switching on is the guest's own choice. */
function moderation(participant, device, key, off) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'btn small moderate';
  button.textContent = device === 'microphone' ? '🎙✕' : '📷✕';
  button.title = api.Text(key);
  button.disabled = off;
  button.addEventListener('click', () => api.SwitchOffAsync(participant.peer, device === 'microphone', device === 'camera').catch(report));
  return button;
}

/** A small label. */
function badge(text, kind) {
  const span = document.createElement('span');
  span.className = 'badge' + (kind ? ' ' + kind : '');
  span.textContent = text;
  return span;
}

/** Draws the position of the film. */
function drawTime() {
  const video = $('video');
  const duration = Number.isFinite(video.duration) ? video.duration : 0;
  $('time').textContent = clock(video.currentTime) + ' / ' + clock(duration);
  if (document.activeElement !== $('timeline') && duration > 0) {
    $('timeline').value = String(Math.round((video.currentTime / duration) * 1000));
  }
}

/** Writes seconds as h:mm:ss or m:ss. */
function clock(seconds) {
  const s = Math.max(0, Math.floor(seconds || 0));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const rest = String(s % 60).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${rest}` : `${m}:${rest}`;
}

/** Answers the service worker: the size of the film and its bytes. */
function onWorkerMessage(e) {
  const message = e.data;
  const port = e.ports[0];
  if (!port || !streams) {
    return;
  }
  if (message.type === 'length') {
    const name = snapshot?.media?.name ?? '';
    const extension = name.includes('.') ? name.split('.').pop().toLowerCase() : '';
    port.postMessage({ length: streams.Length(message.stream), type: TYPES[extension] ?? '' });
  } else if (message.type === 'read') {
    const request = nextRead++;
    reads.set(request, port);
    streams.Read(request, message.stream, message.offset, message.count);
  }
}
