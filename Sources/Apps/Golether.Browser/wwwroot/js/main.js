// Starts Golether in a browser: reads the servers, takes the service worker that streams the film to a guest, starts
// .NET, gives it the modules of the page and lets it draw the screens with Avalonia.

import { dotnet } from '../_framework/dotnet.js';
import * as conference from './conference.js';
import * as files from './files.js';
import * as keys from './keys.js';
import * as page from './page.js';
import * as player from './player.js';
import * as rtc from './rtc.js';
import * as softKeyboard from './softkeyboard.js';
import * as stage from './stage.js';

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

  page.setConfig(await (await fetch('./config.json', { cache: 'no-store' })).text());
  await takeServiceWorker();

  const runtime = await dotnet.create();
  runtime.setModuleImports('golether', { rtc, conference, player, files, keys, page, softKeyboard, stage });
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const api = exports.Golether.Browser.Interop;
  rtc.bind(api.RtcCallbacks);
  conference.bind(api.ConferenceCallbacks);
  player.bind(api.PlayerCallbacks);
  page.bind(api.StreamCallbacks, api.PageCallbacks);
  softKeyboard.bind(api.SoftKeyboardCallbacks);
  await runtime.runMain();
} catch (e) {
  console.error(e);
  page.failed(e?.message ?? 'Start');
}
