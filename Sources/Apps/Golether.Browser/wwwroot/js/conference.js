// The cameras, the voices and the data channel of the page, as .NET commands them: BrowserConferenceMedia decides,
// this module only holds the objects of the browser. One RTCPeerConnection per participant, as the applications keep
// one webrtcbin per participant: VP8 and Opus first, video, then audio, then the "golether" data channel. The browser
// checks the DTLS certificate of the other side against the fingerprint of its description, and the descriptions
// travel only over the authenticated session, so a track or a channel that opens comes from the named participant.

const DATA_LABEL = 'golether';
const HIGH_WATER = 1024 * 1024;
const STATS_PERIOD = 1000;
const LEVEL_PERIOD = 100;
const OUTCOME = { off: 0, started: 1, denied: 2, missing: 3, failed: 4 };
const DEVICE = { camera: 0, microphone: 1 };
const LINK = { connecting: 0, connected: 1, failed: 2 };
// Only ideals: a camera that cannot give exactly these still captures, and the sender keeps the rate of the
// applications (MAX_FRAME_RATE) whatever the camera gives.
const CAMERA = { width: { ideal: 640 }, height: { ideal: 360 }, frameRate: { ideal: 15 } };
const MICROPHONE = { echoCancellation: true, noiseSuppression: true, autoGainControl: true, channelCount: 1 };
const ECONOMY_HEIGHT = 180;
const ECONOMY_FRAME_RATE = 10;
const MAX_FRAME_RATE = 15;

let callbacks = null;
let audio = null;
let deviceList = [];
let statsTimer = 0;
let levelTimer = 0;
let changed = () => {};
const peers = new Map();
const local = { camera: null, microphone: null, prepared: null, analyser: null, source: null, samples: null };

/** Takes the exports of .NET that receive what happens here. */
export function bind(conferenceCallbacks) {
  callbacks = conferenceCallbacks;
  navigator.mediaDevices?.addEventListener?.('devicechange', refreshDevices);
}

/** Lets the page redraw its tiles when a picture appears or goes. */
export function onChange(handler) {
  changed = handler;
}

/** The picture of this device's camera, or null. */
export function localVideo() {
  return local.camera ? new MediaStream([local.camera]) : null;
}

/** The picture of a participant, or null. */
export function remoteVideo(peer) {
  return peers.get(peer)?.remoteVideo ?? null;
}

/**
 * Called from a click: wakes the sound and asks for the devices while the browser still counts the click as the
 * user's (a phone gives the camera, the microphone and the sound only then). The devices are taken over by the next
 * setDevices.
 */
export function prepare(camera, microphone) {
  unlock();
  const wantCamera = camera && !isLive(local.camera);
  const wantMicrophone = microphone && !isLive(local.microphone);
  if (!local.prepared && (wantCamera || wantMicrophone)) {
    local.prepared = acquire(wantCamera, wantMicrophone, null, null);
  }
}

/** Wakes the sound of the page; called from clicks. */
export function unlock() {
  if (!audio) {
    const Context = window.AudioContext ?? window.webkitAudioContext;
    if (!Context) {
      return;
    }
    audio = new Context();
    audio.onstatechange = () => peers.forEach(route);
  }
  if (audio.state !== 'running') {
    audio.resume().catch(() => {});
  }
}

/** Makes the camera and the microphone match the wanted state; gives the outcomes, the camera in the low bits. */
export async function setDevices(camera, microphone, cameraId, microphoneId) {
  const outcome = { camera: OUTCOME.off, microphone: OUTCOME.off };
  const prepared = local.prepared ? await local.prepared : null;
  local.prepared = null;

  for (const kind of ['camera', 'microphone']) {
    const wanted = kind === 'camera' ? camera : microphone;
    const got = prepared?.[kind];
    if (!got) {
      continue;
    }
    if (wanted && got.track && !isLive(local[kind])) {
      setTrack(kind, got.track);
    } else {
      got.track?.stop();
      if (wanted && !isLive(local[kind])) {
        outcome[kind] = got.outcome;
      }
    }
  }

  const want = {
    camera: camera && outcome.camera === OUTCOME.off && !isLive(local.camera, cameraId),
    microphone: microphone && outcome.microphone === OUTCOME.off && !isLive(local.microphone, microphoneId),
  };
  if (want.camera || want.microphone) {
    const got = await acquire(want.camera, want.microphone, cameraId, microphoneId);
    for (const kind of ['camera', 'microphone']) {
      if (got[kind]?.track) {
        setTrack(kind, got[kind].track);
      } else if (got[kind]) {
        outcome[kind] = got[kind].outcome;
      }
    }
  }

  if (!camera) {
    stopTrack('camera');
  }
  if (!microphone) {
    stopTrack('microphone');
  }
  if (camera && isLive(local.camera)) {
    outcome.camera = OUTCOME.started;
  }
  if (microphone && isLive(local.microphone)) {
    outcome.microphone = OUTCOME.started;
  }
  return outcome.camera | (outcome.microphone << 4);
}

/** The devices the page knows of, as kind|id|name. */
export function devices() {
  return deviceList;
}

/** Opens a connection with a participant; the offerer makes the offer at once. */
export function open(peer, isOfferer, iceServers) {
  close(peer);
  const servers = JSON.parse(iceServers).map((s) => (s.username ? { urls: s.url, username: s.username, credential: s.credential } : { urls: s.url }));
  const pc = new RTCPeerConnection({ iceServers: servers, bundlePolicy: 'max-bundle' });
  const entry = {
    pc, dc: null, video: null, audio: null, described: false, outgoing: [], incoming: [],
    economy: false, volume: 1, remoteVideo: null, sink: null, source: null, gain: null, stats: null, level: null,
  };
  peers.set(peer, entry);

  pc.onicecandidate = (e) => {
    if (e.candidate?.candidate) {
      const payload = `${e.candidate.sdpMLineIndex ?? 0}\n${e.candidate.candidate}`;
      if (entry.described) {
        callbacks.OnSignal(peer, 'candidate', payload);
      } else {
        entry.outgoing.push(payload);
      }
    }
  };
  pc.onconnectionstatechange = () => {
    if (peers.get(peer) !== entry) {
      return;
    }
    const state = pc.connectionState;
    if (state === 'connected') {
      callbacks.OnState(peer, LINK.connected);
    } else if (state === 'failed') {
      callbacks.OnState(peer, LINK.failed);
    } else if (state === 'disconnected') {
      callbacks.OnState(peer, LINK.connecting);
    }
  };
  pc.ontrack = (e) => onTrack(peer, entry, e.track);
  pc.ondatachannel = (e) => {
    if (e.channel.label === DATA_LABEL && !entry.dc) {
      wireChannel(peer, entry, e.channel);
    } else {
      e.channel.close();
    }
  };

  if (isOfferer) {
    entry.video = pc.addTransceiver('video', { direction: 'sendrecv' });
    entry.audio = pc.addTransceiver('audio', { direction: 'sendrecv' });
    preferCodecs(entry);
    attachLocal(entry);
    wireChannel(peer, entry, pc.createDataChannel(DATA_LABEL, { ordered: true }));
    describe(peer, entry, 'offer').catch(() => fail(peer, entry));
  }
  startTimers();
}

/** Applies a description of the other side; an offer is answered. */
export async function applyDescription(peer, kind, sdp) {
  const entry = peers.get(peer);
  if (!entry) {
    throw new Error('Unknown');
  }

  await entry.pc.setRemoteDescription({ type: kind, sdp });
  if (kind === 'offer') {
    for (const transceiver of entry.pc.getTransceivers()) {
      const media = transceiver.receiver.track?.kind;
      if (media === 'video' && !entry.video) {
        entry.video = transceiver;
      } else if (media === 'audio' && !entry.audio) {
        entry.audio = transceiver;
      }
    }
    for (const transceiver of [entry.video, entry.audio]) {
      if (transceiver) {
        transceiver.direction = 'sendrecv';
      }
    }
    preferCodecs(entry);
    attachLocal(entry);
    await describe(peer, entry, 'answer');
  }

  for (const candidate of entry.incoming.splice(0)) {
    await entry.pc.addIceCandidate(candidate).catch(() => {});
  }
}

/** Adds a candidate of the other side; one that comes before the description waits for it. */
export function addCandidate(peer, line, candidate) {
  const entry = peers.get(peer);
  if (!entry) {
    return;
  }
  const value = { candidate, sdpMLineIndex: line };
  if (entry.pc.remoteDescription) {
    entry.pc.addIceCandidate(value).catch(() => {});
  } else {
    entry.incoming.push(value);
  }
}

/** Closes the connection with a participant and takes its picture and voice off the page. */
export function close(peer) {
  const entry = peers.get(peer);
  if (!entry) {
    return;
  }
  peers.delete(peer);
  entry.dc?.close();
  entry.pc.close();
  if (entry.sink) {
    entry.sink.srcObject = null;
  }
  entry.source?.disconnect();
  entry.gain?.disconnect();
  if (peers.size === 0) {
    stopTimers();
  }
  changed();
}

/** Sends one message over the data channel of a participant; false when it is closed or full. */
export function send(peer, view) {
  const dc = peers.get(peer)?.dc;
  if (!dc || dc.readyState !== 'open' || dc.bufferedAmount > HIGH_WATER) {
    return false;
  }
  dc.send(view.slice());
  return true;
}

/** Switches the camera stream sent to a participant between the economy stream and the full one. */
export function setEconomy(peer, economy) {
  const entry = peers.get(peer);
  if (entry) {
    entry.economy = economy;
    applyEconomy(entry);
  }
}

/** Sets the loudness of the voice of a participant. */
export function setVolume(peer, volume) {
  const entry = peers.get(peer);
  if (!entry) {
    return;
  }
  entry.volume = volume;
  if (entry.gain) {
    entry.gain.gain.value = volume;
  }
  if (entry.sink) {
    entry.sink.volume = Math.min(1, volume);
  }
}

/** Closes every connection and stops the devices, the ones asked for in advance too. */
export function stopAll() {
  for (const peer of [...peers.keys()]) {
    close(peer);
  }
  stopTrack('camera');
  stopTrack('microphone');
  const prepared = local.prepared;
  local.prepared = null;
  prepared?.then((got) => {
    got.camera?.track?.stop();
    got.microphone?.track?.stop();
  });
  stopTimers();
  changed();
}

/** Whether a track captures (and is the chosen device, when one is named). */
function isLive(track, id) {
  return Boolean(track) && track.readyState === 'live' && (!id || track.getSettings().deviceId === id);
}

/** The outcome of a refused request for a device. */
function outcomeOf(error) {
  switch (error?.name) {
    case 'NotAllowedError':
    case 'SecurityError':
      return OUTCOME.denied;
    case 'NotFoundError':
    case 'OverconstrainedError':
      return OUTCOME.missing;
    default:
      return OUTCOME.failed;
  }
}

/** Asks the browser for devices: both at once (one question), each alone when one of them is missing. */
async function acquire(camera, microphone, cameraId, microphoneId) {
  const video = camera ? { ...CAMERA, ...(cameraId ? { deviceId: { exact: cameraId } } : {}) } : false;
  const sound = microphone ? { ...MICROPHONE, ...(microphoneId ? { deviceId: { exact: microphoneId } } : {}) } : false;
  const result = { camera: null, microphone: null };
  if (!navigator.mediaDevices?.getUserMedia) {
    result.camera = camera ? { track: null, outcome: OUTCOME.missing } : null;
    result.microphone = microphone ? { track: null, outcome: OUTCOME.missing } : null;
    return result;
  }

  try {
    const stream = await navigator.mediaDevices.getUserMedia({ video, audio: sound });
    result.camera = camera ? found(stream.getVideoTracks()[0]) : null;
    result.microphone = microphone ? found(stream.getAudioTracks()[0]) : null;
  } catch (error) {
    if (camera && microphone && outcomeOf(error) !== OUTCOME.denied) {
      const one = await acquire(true, false, cameraId, null);
      const other = await acquire(false, true, null, microphoneId);
      result.camera = one.camera;
      result.microphone = other.microphone;
    } else {
      const outcome = outcomeOf(error);
      result.camera = camera ? { track: null, outcome } : null;
      result.microphone = microphone ? { track: null, outcome } : null;
    }
  }
  refreshDevices();
  return result;
}

/** The result for a track the browser gave, or a missing device when it gave none. */
function found(track) {
  return track ? { track, outcome: OUTCOME.started } : { track: null, outcome: OUTCOME.missing };
}

/** Makes a captured track the device of the page and puts it into every connection. */
function setTrack(kind, track) {
  stopTrack(kind);
  local[kind] = track;
  track.onended = () => {
    if (local[kind] === track) {
      local[kind] = null;
      peers.forEach(attachLocal);
      if (kind === 'microphone') {
        stopLocalLevel();
      }
      changed();
      callbacks.OnDeviceEnded(kind === 'camera' ? DEVICE.camera : DEVICE.microphone);
    }
  };
  peers.forEach(attachLocal);
  if (kind === 'microphone') {
    startLocalLevel(track);
  }
  changed();
}

/** Stops a device of the page: its light goes off and the connections send nothing for it. */
function stopTrack(kind) {
  const track = local[kind];
  if (!track) {
    return;
  }
  track.onended = null;
  track.stop();
  local[kind] = null;
  peers.forEach(attachLocal);
  if (kind === 'microphone') {
    stopLocalLevel();
  }
  changed();
}

/** Puts the devices of the page into the senders of a connection. */
function attachLocal(entry) {
  entry.video?.sender.replaceTrack(local.camera ?? null).then(() => applyEconomy(entry)).catch(() => {});
  entry.audio?.sender.replaceTrack(local.microphone ?? null).catch(() => {});
}

/**
 * Puts VP8 and Opus, the codecs of the applications, first where the browser lets it choose. The other codecs stay
 * behind them: Firefox writes a broken offer when codecs are taken away, and the other side picks a common one anyway.
 */
function preferCodecs(entry) {
  for (const [transceiver, kind, mime] of [[entry.video, 'video', 'video/vp8'], [entry.audio, 'audio', 'audio/opus']]) {
    if (!transceiver?.setCodecPreferences || !RTCRtpReceiver.getCapabilities) {
      continue;
    }
    const all = RTCRtpReceiver.getCapabilities(kind)?.codecs ?? [];
    const codecs = [...all.filter((c) => c.mimeType.toLowerCase() === mime), ...all.filter((c) => c.mimeType.toLowerCase() !== mime)];
    if (codecs.length > 0) {
      try {
        transceiver.setCodecPreferences(codecs);
      } catch {
        // A browser that refuses keeps its own order; VP8 and Opus are in every one.
      }
    }
  }
}

/** Makes and sends the offer or the answer, then the candidates found meanwhile. */
async function describe(peer, entry, kind) {
  const description = kind === 'offer' ? await entry.pc.createOffer() : await entry.pc.createAnswer();
  await entry.pc.setLocalDescription(description);
  if (peers.get(peer) !== entry) {
    return;
  }
  callbacks.OnSignal(peer, kind, entry.pc.localDescription.sdp);
  entry.described = true;
  for (const payload of entry.outgoing.splice(0)) {
    callbacks.OnSignal(peer, 'candidate', payload);
  }
}

/** Reports a connection that could not even be described. */
function fail(peer, entry) {
  if (peers.get(peer) === entry) {
    callbacks.OnState(peer, LINK.failed);
  }
}

/** Connects the data channel of a participant to .NET. */
function wireChannel(peer, entry, dc) {
  entry.dc = dc;
  dc.binaryType = 'arraybuffer';
  dc.onopen = () => {
    if (peers.get(peer) === entry) {
      callbacks.OnDataOpened(peer);
    }
  };
  dc.onclose = () => {
    if (peers.get(peer) === entry) {
      callbacks.OnDataClosed(peer);
    }
  };
  dc.onmessage = (e) => {
    if (peers.get(peer) === entry && e.data instanceof ArrayBuffer) {
      callbacks.OnData(peer, new Uint8Array(e.data));
    }
  };

  // The answering side gets the channel of the other side already open, and no open event follows.
  if (dc.readyState === 'open' && peers.get(peer) === entry) {
    callbacks.OnDataOpened(peer);
  }
}

/** Shows the picture or plays the voice of a participant. */
function onTrack(peer, entry, track) {
  if (peers.get(peer) !== entry) {
    return;
  }
  const stream = new MediaStream([track]);
  if (track.kind === 'video') {
    entry.remoteVideo = stream;
    track.onunmute = () => changed();
    changed();
    return;
  }

  // An element keeps the remote sound flowing (Chrome plays a WebRTC track through Web Audio only when an element
  // holds it too); it plays itself only while Web Audio cannot.
  entry.sink = new Audio();
  entry.sink.srcObject = stream;
  entry.sink.muted = true;
  entry.sink.play().catch(() => {});
  if (audio) {
    entry.source = audio.createMediaStreamSource(stream);
    entry.gain = audio.createGain();
    entry.gain.gain.value = entry.volume;
    entry.source.connect(entry.gain).connect(audio.destination);
  }
  route(entry);
}

/** Plays a voice through Web Audio when it runs (the loudness then works on every device), else through the element. */
function route(entry) {
  if (!entry.sink) {
    return;
  }
  const webAudio = Boolean(entry.gain) && audio?.state === 'running';
  entry.sink.muted = webAudio;
  if (entry.gain) {
    entry.gain.gain.value = webAudio ? entry.volume : 0;
  }
}

/** Applies the camera stream chosen for a participant to its video sender. */
function applyEconomy(entry) {
  const sender = entry.video?.sender;
  if (!sender?.track) {
    return;
  }
  const parameters = sender.getParameters();
  if (!parameters.encodings || parameters.encodings.length === 0) {
    parameters.encodings = [{}];
  }
  const encoding = parameters.encodings[0];
  if (entry.economy) {
    encoding.scaleResolutionDownBy = Math.max(1, (sender.track.getSettings().height || 360) / ECONOMY_HEIGHT);
    encoding.maxFramerate = ECONOMY_FRAME_RATE;
  } else {
    encoding.scaleResolutionDownBy = 1;
    encoding.maxFramerate = MAX_FRAME_RATE;
  }
  sender.setParameters(parameters).catch(() => {});
}

/** Starts measuring the connections and the voices. */
function startTimers() {
  if (!statsTimer) {
    statsTimer = setInterval(reportStats, STATS_PERIOD);
  }
  if (!levelTimer) {
    levelTimer = setInterval(reportLevels, LEVEL_PERIOD);
  }
}

/** Stops measuring. */
function stopTimers() {
  clearInterval(statsTimer);
  clearInterval(levelTimer);
  statsTimer = 0;
  levelTimer = 0;
}

/** Reports the statistics of every made connection: the loss of the video sent and the round trip. */
async function reportStats() {
  for (const [peer, entry] of peers) {
    if (entry.pc.connectionState !== 'connected') {
      continue;
    }
    let report;
    try {
      report = await entry.pc.getStats();
    } catch {
      continue;
    }

    const now = { sent: NaN, lost: NaN, rtt: NaN, pairRtt: NaN, videoSent: NaN, videoReceived: NaN, audioSent: NaN, audioReceived: NaN };
    report.forEach((s) => {
      const kind = s.kind ?? s.mediaType;
      if (s.type === 'outbound-rtp' && kind === 'video') {
        now.sent = s.packetsSent;
        now.videoSent = s.framesSent ?? s.framesEncoded;
      } else if (s.type === 'outbound-rtp' && kind === 'audio') {
        now.audioSent = s.packetsSent;
      } else if (s.type === 'inbound-rtp' && kind === 'video') {
        now.videoReceived = s.framesDecoded ?? s.framesReceived;
      } else if (s.type === 'inbound-rtp' && kind === 'audio') {
        now.audioReceived = s.packetsReceived;
      } else if (s.type === 'remote-inbound-rtp' && kind === 'video') {
        now.lost = s.packetsLost;
        if (typeof s.roundTripTime === 'number') {
          now.rtt = s.roundTripTime;
        }
      } else if (s.type === 'candidate-pair' && s.nominated && s.state === 'succeeded' && typeof s.currentRoundTripTime === 'number') {
        now.pairRtt = s.currentRoundTripTime;
      }
    });

    const before = entry.stats;
    entry.stats = now;
    const sent = before ? now.sent - before.sent : NaN;
    const lost = before ? now.lost - before.lost : NaN;
    const loss = sent > 0 && lost >= 0 ? Math.min(1, lost / sent) : NaN;
    const rtt = Number.isFinite(now.rtt) ? now.rtt : now.pairRtt;
    if (peers.get(peer) === entry) {
      callbacks.OnStats(peer, loss, rtt, now.videoSent, now.videoReceived, now.audioSent, now.audioReceived);
    }
  }
}

/** Reports the level of every voice and of the microphone; a source that sent nothing new counts as silent. */
function reportLevels() {
  for (const [peer, entry] of peers) {
    if (entry.pc.connectionState !== 'connected' || !entry.audio?.receiver.getSynchronizationSources) {
      continue;
    }
    const source = entry.audio.receiver.getSynchronizationSources()[0];
    const fresh = source && source.timestamp !== entry.level;
    entry.level = source?.timestamp ?? null;
    callbacks.OnLevel(peer, fresh ? source.audioLevel ?? 0 : 0);
  }

  if (local.analyser) {
    local.analyser.getFloatTimeDomainData(local.samples);
    let sum = 0;
    for (const sample of local.samples) {
      sum += sample * sample;
    }
    callbacks.OnLevel('', Math.sqrt(sum / local.samples.length));
  }
}

/** Measures the level of the microphone of this device. */
function startLocalLevel(track) {
  stopLocalLevel();
  if (!audio) {
    return;
  }
  local.source = audio.createMediaStreamSource(new MediaStream([track]));
  local.analyser = audio.createAnalyser();
  local.analyser.fftSize = 1024;
  local.samples = new Float32Array(local.analyser.fftSize);
  local.source.connect(local.analyser);
  startTimers();
}

/** Stops measuring the microphone. */
function stopLocalLevel() {
  local.source?.disconnect();
  local.source = null;
  local.analyser = null;
  local.samples = null;
}

/** Reads the list of devices again: names appear once a permission was given. */
function refreshDevices() {
  navigator.mediaDevices?.enumerateDevices?.()
    .then((list) => {
      deviceList = list
        .filter((d) => d.kind === 'videoinput' || d.kind === 'audioinput')
        .map((d) => `${d.kind === 'videoinput' ? DEVICE.camera : DEVICE.microphone}|${d.deviceId}|${d.label}`);
    })
    .catch(() => {});
}
