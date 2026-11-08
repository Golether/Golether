// Starts Golether in a browser: reads the servers, takes the service worker that streams the film to a guest,
// starts .NET, connects the WebRTC modules to it and hands the page to app.js.

import { dotnet } from '../_framework/dotnet.js';
import * as app from './app.js';
import * as conference from './conference.js';
import * as files from './files.js';
import * as keys from './keys.js';
import * as player from './player.js';
import * as rtc from './rtc.js';

/** Shows a failure of the start; there are no language packs yet, so the reason is a code. */
function failed(code) {
  document.getElementById('loading').dataset.failed = code;
}

/** Takes the service worker and waits until it controls the page. */
async function takeServiceWorker() {
  if (!('serviceWorker' in navigator)) {
    throw new Error('NoServiceWorker');
  }
  await navigator.serviceWorker.register('./sw.js', { scope: './' });
  if (!navigator.serviceWorker.controller) {
    await new Promise((resolve) => navigator.serviceWorker.addEventListener('controllerchange', resolve, { once: true }));
  }
}

try {
  if (!('RTCPeerConnection' in window) || !crypto?.subtle) {
    throw new Error('NoWebRtc');
  }

  const config = await (await fetch('./config.json', { cache: 'no-store' })).json();
  await takeServiceWorker();

  const runtime = await dotnet.create();
  runtime.setModuleImports('golether', { rtc, conference, player, files, keys, page: app.page });
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  rtc.bind(exports.Golether.Browser.Interop.RtcCallbacks);
  conference.bind(exports.Golether.Browser.Interop.ConferenceCallbacks);
  await runtime.runMain();

  await app.start(exports, config);
} catch (e) {
  console.error(e);
  failed(e?.message ?? 'Start');
}
