// The WebRTC side of Golether in a browser. The rendezvous server only passes the descriptions of a connection
// (signalling over a WebSocket); the session itself goes directly between the browsers in data channels, encrypted
// with DTLS. Who is on the other end is proven with the device keys: each side signs the fingerprints of both DTLS
// certificates, so a server that swapped the descriptions would be caught. A failure is reported by code, never by
// text: 'ForeignKey' or a name of RendezvousError ('RoomNotFound', 'Unreachable', ...).

import * as keys from './keys.js';

const AUTH_LABEL = 'golether-auth';
const AUTH_CONTEXT = 'golether-webrtc-v1';
const SIGNAL_CONTEXT = 'golether-signal-v1';
const PROTOCOL_VERSION = 1;
const HIGH_WATER = 1024 * 1024;
const LOW_WATER = 256 * 1024;
const MAX_CHANNELS_PER_PEER = 24;
const CONNECT_TIMEOUT = 30000;

let callbacks = null;
const channels = new Map();
let nextChannel = 1;
let host = null;
const links = new Map();

/** Takes the exports of .NET that receive channels, messages and closings. */
export function bind(rtcCallbacks) {
  callbacks = rtcCallbacks;
}

/** Makes an error with a code. */
function fail(code) {
  return new Error(code);
}

/** Waits for a promise no longer than a time; fails with the code then. */
function within(promise, ms, code) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(fail(code)), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

/** The DTLS fingerprints of a description, in a form both sides write the same way. */
function fingerprints(sdp) {
  return sdp.split(/\r?\n/)
    .filter((line) => line.startsWith('a=fingerprint:'))
    .map((line) => line.slice('a=fingerprint:'.length).trim().toUpperCase())
    .sort()
    .join(',');
}

/** The text both sides sign: the fingerprints of the host and of the guest and the room. */
function proofText(pc, isHost, room) {
  const local = fingerprints(pc.localDescription.sdp);
  const remote = fingerprints(pc.remoteDescription.sdp);
  return [AUTH_CONTEXT, isHost ? local : remote, isHost ? remote : local, room].join('\n');
}

/** A new peer connection with a certificate of its own and the STUN server of the session. */
async function createPeer(stunUrl) {
  const certificate = await RTCPeerConnection.generateCertificate({ name: 'ECDSA', namedCurve: 'P-256' });
  return new RTCPeerConnection({ iceServers: [{ urls: stunUrl }], certificates: [certificate] });
}

/** Adds candidates once the remote description is known; the earlier ones wait. */
function candidateQueue(pc) {
  const waiting = [];
  return {
    async add(candidate) {
      if (pc.remoteDescription) {
        await pc.addIceCandidate(candidate).catch(() => {});
      } else {
        waiting.push(candidate);
      }
    },
    async flush() {
      for (const candidate of waiting.splice(0)) {
        await pc.addIceCandidate(candidate).catch(() => {});
      }
    },
  };
}

/** Waits for the next message of a data channel. */
function nextMessage(dc) {
  return new Promise((resolve, reject) => {
    dc.addEventListener('message', (e) => resolve(e.data), { once: true });
    dc.addEventListener('close', () => reject(fail('Unreachable')), { once: true });
  });
}

/** Waits until a data channel opens. */
function opened(dc) {
  if (dc.readyState === 'open') {
    return Promise.resolve();
  }
  return new Promise((resolve, reject) => {
    dc.addEventListener('open', () => resolve(), { once: true });
    dc.addEventListener('close', () => reject(fail('Unreachable')), { once: true });
  });
}

/** Reads a proof of the other side and checks it; gives the identifier it proved, or null. */
async function readProof(dc, text) {
  let proof;
  try {
    proof = JSON.parse(await within(nextMessage(dc), 15000, 'Timeout'));
  } catch {
    return null;
  }
  if (typeof proof?.key !== 'string' || typeof proof?.signature !== 'string') {
    return null;
  }
  return keys.verify(proof.key, text, proof.signature);
}

/** Sends the proof of this device. */
async function sendProof(dc, text) {
  dc.send(JSON.stringify({ key: await keys.publicKey(), signature: await keys.sign(text) }));
}

/** Gives an open data channel a number and connects its messages and closing to .NET. */
function register(dc) {
  const id = nextChannel++;
  channels.set(id, dc);
  dc.binaryType = 'arraybuffer';
  dc.bufferedAmountLowThreshold = LOW_WATER;
  dc.onmessage = (e) => {
    if (e.data instanceof ArrayBuffer) {
      callbacks.OnData(id, new Uint8Array(e.data));
    }
  };
  dc.onclose = () => {
    if (channels.delete(id)) {
      callbacks.OnClosed(id);
    }
  };
  return id;
}

/** Sends one message; the bytes are copied, .NET reuses its memory. Tells whether the channel is full. */
export function send(id, view) {
  const dc = channels.get(id);
  if (!dc || dc.readyState !== 'open') {
    throw fail('Unreachable');
  }
  dc.send(view.slice());
  return dc.bufferedAmount > HIGH_WATER;
}

/** Resolves when the channel has sent most of what it was given. */
export function drain(id) {
  const dc = channels.get(id);
  if (!dc || dc.bufferedAmount <= HIGH_WATER) {
    return Promise.resolve();
  }
  return new Promise((resolve) => {
    const done = () => {
      dc.removeEventListener('bufferedamountlow', done);
      dc.removeEventListener('close', done);
      resolve();
    };
    dc.addEventListener('bufferedamountlow', done);
    dc.addEventListener('close', done);
  });
}

/** Closes a channel this side no longer needs. */
export function close(id) {
  const dc = channels.get(id);
  channels.delete(id);
  dc?.close();
}

/** A WebSocket of the signalling: waits for the greeting, then passes frames and answers waits. */
class Signal {
  static open(url) {
    return new Promise((resolve, reject) => {
      let ws;
      try {
        ws = new WebSocket(url);
      } catch {
        reject(fail('Unreachable'));
        return;
      }
      const signal = new Signal(ws);
      const timer = setTimeout(() => {
        ws.close();
        reject(fail('Unreachable'));
      }, 10000);
      ws.onerror = () => {
        clearTimeout(timer);
        reject(fail('Unreachable'));
      };
      ws.onmessage = (e) => {
        let frame;
        try {
          frame = JSON.parse(e.data);
        } catch {
          return;
        }
        if (!signal.hello) {
          clearTimeout(timer);
          if (frame.$t !== 'server-hello' || frame.version !== PROTOCOL_VERSION || typeof frame.nonce !== 'string') {
            ws.close();
            reject(fail(frame.$t === 'error' ? frame.error : 'UnsupportedVersion'));
            return;
          }
          signal.hello = frame;
          resolve(signal);
          return;
        }
        signal.dispatch(frame);
      };
      ws.onclose = () => {
        clearTimeout(timer);
        signal.closed = true;
        for (const waiter of signal.waiters.splice(0)) {
          waiter.reject(fail('Unreachable'));
        }
        signal.onclose?.();
      };
    });
  }

  constructor(ws) {
    this.ws = ws;
    this.hello = null;
    this.closed = false;
    this.waiters = [];
    this.queue = [];
    this.handler = null;
    this.onclose = null;
  }

  set onframe(handler) {
    this.handler = handler;
    for (const frame of this.queue.splice(0)) {
      handler(frame);
    }
  }

  send(frame) {
    if (this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(frame));
    }
  }

  dispatch(frame) {
    if (frame.$t === 'error') {
      const error = fail(typeof frame.error === 'string' ? frame.error : 'BadRequest');
      for (const waiter of this.waiters.splice(0)) {
        waiter.reject(error);
      }
      return;
    }
    const index = this.waiters.findIndex((w) => w.type === frame.$t);
    if (index >= 0) {
      this.waiters.splice(index, 1)[0].resolve(frame);
    } else if (this.handler) {
      this.handler(frame);
    } else {
      this.queue.push(frame);
    }
  }

  next(type, ms = 20000) {
    if (this.closed) {
      return Promise.reject(fail('Unreachable'));
    }
    return within(new Promise((resolve, reject) => this.waiters.push({ type, resolve, reject })), ms, 'Timeout');
  }

  close() {
    this.ws.close();
  }
}

// ---------------------------------------------------------------------------------------------------- the guest

/**
 * Opens a data channel to the host of a room. The first call meets the host and proves its key against the
 * invitation; the next calls open more channels in the same connection.
 */
export async function connect(signalUrl, stunUrl, room, expectedPeer, purpose) {
  const key = room + '|' + expectedPeer;
  let link = links.get(key);
  if (!link || link.dead) {
    link = meetHost(signalUrl, stunUrl, room, expectedPeer);
    links.set(key, link);
  }

  try {
    await link.ready;
  } catch (e) {
    links.delete(key);
    throw e;
  }

  const dc = link.pc.createDataChannel(String(purpose), { ordered: true });
  await within(opened(dc), 15000, 'Timeout');
  return register(dc);
}

/** Meets the host through the server, makes the connection and checks who the host is. */
function meetHost(signalUrl, stunUrl, room, expectedPeer) {
  const link = { pc: null, dead: false, ready: null };
  link.ready = (async () => {
    const signal = await Signal.open(signalUrl);
    try {
      signal.send({ $t: 'connect', room });
      await signal.next('meeting');

      const pc = await createPeer(stunUrl);
      link.pc = pc;
      const candidates = candidateQueue(pc);
      pc.onicecandidate = (e) => {
        if (e.candidate) {
          signal.send({ $t: 'signal', data: JSON.stringify({ candidate: e.candidate }) });
        }
      };
      pc.onconnectionstatechange = () => {
        if (pc.connectionState === 'failed' || pc.connectionState === 'closed') {
          link.dead = true;
          pc.close();
        }
      };
      signal.onframe = async (frame) => {
        if (frame.$t !== 'signal' || typeof frame.data !== 'string') {
          return;
        }
        const message = JSON.parse(frame.data);
        if (message.answer && !pc.remoteDescription) {
          await pc.setRemoteDescription(message.answer);
          await candidates.flush();
        } else if (message.candidate) {
          await candidates.add(message.candidate);
        }
      };

      const auth = pc.createDataChannel(AUTH_LABEL, { ordered: true });
      await pc.setLocalDescription(await pc.createOffer());
      signal.send({ $t: 'signal', data: JSON.stringify({ offer: pc.localDescription }) });
      await within(opened(auth), CONNECT_TIMEOUT, 'PunchFailed');

      const text = proofText(pc, false, room);
      await sendProof(auth, text);
      const proven = await readProof(auth, text);
      if (proven !== expectedPeer) {
        link.dead = true;
        pc.close();
        throw fail('ForeignKey');
      }
    } catch (e) {
      link.dead = true;
      link.pc?.close();
      throw e;
    } finally {
      signal.close();
    }
  })();
  return link;
}

// ----------------------------------------------------------------------------------------------------- the host

/** Takes a room and starts accepting guests. */
export async function hostOpen(signalUrl, stunUrl, room) {
  hostClose();
  const state = { signalUrl, stunUrl, room, meetings: new Map(), peers: new Set(), closed: false, signal: null };
  host = state;
  await registerRoom(state);
}

/** Registers the room on the server; a lost connection is made again while the session lasts. */
async function registerRoom(state) {
  const signal = await Signal.open(state.signalUrl);
  const signature = await keys.sign([SIGNAL_CONTEXT, signal.hello.nonce, state.room].join('\n'));
  signal.send({ $t: 'register', room: state.room, key: await keys.publicKey(), signature });
  try {
    await signal.next('registered');
  } catch (e) {
    signal.close();
    throw e;
  }

  state.signal = signal;
  signal.onframe = (frame) => onHostFrame(state, frame);
  signal.onclose = () => {
    if (!state.closed) {
      setTimeout(() => {
        if (!state.closed) {
          registerRoom(state).catch(() => signal.onclose());
        }
      }, 3000);
    }
  };
}

/** Handles a frame the server sent to the host. */
function onHostFrame(state, frame) {
  if (frame.$t === 'incoming' && typeof frame.meeting === 'string') {
    acceptMeeting(state, frame.meeting).catch(() => state.meetings.get(frame.meeting)?.pc.close());
  } else if (frame.$t === 'signal' && typeof frame.data === 'string') {
    state.meetings.get(frame.meeting)?.onsignal(frame.data);
  } else if (frame.$t === 'closed') {
    const meeting = state.meetings.get(frame.meeting);
    state.meetings.delete(frame.meeting);
    if (meeting && meeting.pc.connectionState !== 'connected') {
      meeting.pc.close();
    }
  }
}

/** Answers a guest: makes the connection, checks the key of the guest, hands its channels to .NET. */
async function acceptMeeting(state, meeting) {
  const pc = await createPeer(state.stunUrl);
  state.peers.add(pc);
  const candidates = candidateQueue(pc);
  let resolveProof;
  const proof = new Promise((resolve) => {
    resolveProof = resolve;
  });
  let channelCount = 0;

  state.meetings.set(meeting, {
    pc,
    async onsignal(data) {
      const message = JSON.parse(data);
      if (message.offer && !pc.remoteDescription) {
        await pc.setRemoteDescription(message.offer);
        await candidates.flush();
        await pc.setLocalDescription(await pc.createAnswer());
        state.signal?.send({ $t: 'signal', meeting, data: JSON.stringify({ answer: pc.localDescription }) });
      } else if (message.candidate) {
        await candidates.add(message.candidate);
      }
    },
  });

  pc.onicecandidate = (e) => {
    if (e.candidate) {
      state.signal?.send({ $t: 'signal', meeting, data: JSON.stringify({ candidate: e.candidate }) });
    }
  };
  pc.onconnectionstatechange = () => {
    if (pc.connectionState === 'failed' || pc.connectionState === 'closed') {
      pc.close();
      state.peers.delete(pc);
    }
  };
  setTimeout(() => {
    if (pc.connectionState !== 'connected') {
      pc.close();
      resolveProof(null);
    }
  }, CONNECT_TIMEOUT + 15000);

  pc.ondatachannel = async (e) => {
    const dc = e.channel;
    dc.binaryType = 'arraybuffer';
    if (dc.label === AUTH_LABEL) {
      try {
        await opened(dc);
        const text = proofText(pc, true, state.room);
        const peer = await readProof(dc, text);
        if (!peer) {
          resolveProof(null);
          pc.close();
          return;
        }
        await sendProof(dc, text);
        resolveProof(peer);
      } catch {
        resolveProof(null);
        pc.close();
      }
      return;
    }

    const peer = await proof;
    if (!peer || ++channelCount > MAX_CHANNELS_PER_PEER || state.closed) {
      dc.close();
      return;
    }
    try {
      await opened(dc);
    } catch {
      return;
    }
    const id = register(dc);
    if (!callbacks.OnChannel(id, peer, Number(dc.label) || 0)) {
      channels.delete(id);
      dc.close();
    }
  };
}

/** Releases the room and closes every connection with guests. */
export function hostClose() {
  if (!host) {
    return;
  }
  const state = host;
  host = null;
  state.closed = true;
  state.signal?.close();
  for (const pc of state.peers) {
    pc.close();
  }
  state.peers.clear();
  state.meetings.clear();
}
