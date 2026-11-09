# avbridge in the browser version

The film of the browser version plays through [avbridge](https://github.com/keishi/avbridge) 2.14.0 (MIT): MP4 and
WebM natively, MKV repackaged for the browser, AVI and the sound the browsers do not decode (AC-3, DTS, …) through
libav.js. It is kept in `wwwroot/lib/avbridge`:

- `dist/element-browser.js` — the bundle of the `<avbridge-video>` element with mediabunny (MPL-2.0) and
  libavjs-webcodecs-bridge (ISC) inside, changed by `patch-avbridge.js`;
- `vendor/libav` — the two libav.js builds of the package (LGPL-2.1-or-later), unchanged; how to rebuild them is in
  `vendor/libav/README.md`;
- `LICENSE`, `NOTICE.md`, `THIRD_PARTY_LICENSES.md` of the package.

The page loads the bundle with the first film, and libav.js only for a file that needs it.

## Updating

```
npm pack avbridge@<version>
tar -xzf avbridge-<version>.tgz
node patch-avbridge.js package/dist/element-browser.js ../wwwroot/lib/avbridge/dist/element-browser.js
```

Then copy `package/vendor/libav/avbridge`, `package/vendor/libav/webcodecs` (without `libav.types.d.ts`) and the
license files over the old ones. The script stops when a change no longer applies: look whether the new version fixed
it and drop that change, or adapt it. Check the films afterwards: MP4, MKV with two AAC tracks and with AC-3, AVI;
playing, pausing, seeking between keyframes and switching the sound track.

## The changes

The comment at the top of `patch-avbridge.js` lists them with their reasons: the sound tracks of the libav decoders, a
seek and a track switch that hung, the ten-second stall after a seek between keyframes, AVI timestamps, and playing on
in a hidden tab.
