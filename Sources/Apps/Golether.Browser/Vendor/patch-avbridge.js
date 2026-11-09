// Patches avbridge 2.14.0 dist/element-browser.js for Golether: node patch-avbridge.js <in> <out>
// 1. Audio tracks of the libav decoders are found by their position among the audio tracks of the probe, not by the
//    probe's id (mediabunny numbers tracks from 1 with the video included, libav numbers streams from 0).
// 2. A seek or a track switch of the hybrid decoder resets the WebCodecs video decoder instead of flushing it: a flush
//    waits for frames the paused renderer never releases, and the seek hangs.
// 3. The renderer drops its queued frames before the decoder seeks, for the same reason.
// 4. After a seek or a track switch the hybrid decoder drops the frames before the target (from the keyframe on):
//    they filled the renderer queue, stopped the demuxer and left the audio without data for ten seconds.
// 5. The element keeps playing in a hidden tab (backgroundBehavior "continue"): a viewer of a shared session must
//    not stop because they switched tabs.
// 6. A packet without pts takes its dts before a made-up time: AVI gives video packets only a dts, and after a seek
//    the made-up times labelled the frames from the keyframe with the target time.
// 7. A decoded frame goes to the browser with tightly packed planes, one right after another. libav pads its rows and
//    leaves gaps between its planes, and Firefox reads the planes of a VideoFrame as if they were packed whatever the
//    layout says: the colours of every film decoded by libav (Xvid, DivX, MPEG-2, …) slid into stripes there.
const fs = require('fs');
const [input, output] = process.argv.slice(2);
let text = fs.readFileSync(input, 'utf8');

function replace(from, to, expected) {
  const count = text.split(from).length - 1;
  if (count !== expected) {
    throw new Error(`expected ${expected} of ${JSON.stringify(from.slice(0, 80))}, found ${count}`);
  }
  text = text.split(from).join(to);
}

replace(
  'const firstAudioTrackId = opts.context.audioTracks[0]?.id;\n  let audioStream = (firstAudioTrackId != null ? streams.find((s) => s.codec_type === libav.AVMEDIA_TYPE_AUDIO && s.index === firstAudioTrackId) : void 0) ?? streams.find((s) => s.codec_type === libav.AVMEDIA_TYPE_AUDIO) ?? null;',
  'const audioStreamFor = (id) => { const all = streams.filter((s) => s.codec_type === libav.AVMEDIA_TYPE_AUDIO); const at = opts.context.audioTracks.findIndex((t) => t.id === id); return (at >= 0 ? all[at] : void 0) ?? null; };\n  let audioStream = audioStreamFor(opts.context.audioTracks[0]?.id) ?? streams.find((s) => s.codec_type === libav.AVMEDIA_TYPE_AUDIO) ?? null;',
  2);

replace(
  'if (audioStream && audioStream.index === trackId) return;\n      const newStream = streams.find(\n        (s) => s.codec_type === libav.AVMEDIA_TYPE_AUDIO && s.index === trackId\n      );',
  'const newStream = audioStreamFor(trackId);\n      if (audioStream && newStream === audioStream) return;',
  2);

replace(
  '  let videoDecoder = null;\n  let videoTimeBase;',
  '  let videoDecoder = null;\n  let videoConfig = null;\n  let videoTimeBase;',
  1);

replace(
  'const config = await bridge.videoStreamToConfig(libav, videoStream);',
  'const config = videoConfig = await bridge.videoStreamToConfig(libav, videoStream);',
  1);

replace(
  '        if (videoDecoder && videoDecoder.state === "configured") {\n          await videoDecoder.flush();\n        }',
  '        if (videoDecoder && videoDecoder.state === "configured") {\n          videoDecoder.reset();\n          videoDecoder.configure(videoConfig);\n        }',
  2);

replace(
  '    await audio.pause().catch(() => {\n    });\n    await handles.seek(timeSec)',
  '    await audio.pause().catch(() => {\n    });\n    renderer.flush();\n    await handles.seek(timeSec)',
  2);

replace(
  '      await audio.pause().catch(() => {\n      });\n      await handles.setAudioTrack(id, currentTime)',
  '      await audio.pause().catch(() => {\n      });\n      renderer.flush();\n      await handles.setAudioTrack(id, currentTime)',
  2);

replace(
  '  let videoConfig = null;\n',
  '  let videoConfig = null;\n  let dropVideoBeforeUs = -Infinity;\n',
  1);

replace(
  'output: (frame) => {\n          opts.renderer.enqueue(frame);',
  'output: (frame) => {\n          if (frame.timestamp < dropVideoBeforeUs) {\n            frame.close();\n            return;\n          }\n          opts.renderer.enqueue(frame);',
  1);

replace(
  '      syntheticVideoUs = Math.round(timeSec * 1e6);',
  '      syntheticVideoUs = Math.round(timeSec * 1e6);\n      dropVideoBeforeUs = Math.round(timeSec * 1e6) - 40000;',
  2);

replace(
  '        ...this._subtitles ? { subtitles: this._subtitles } : {}\n      });',
  '        ...this._subtitles ? { subtitles: this._subtitles } : {},\n        backgroundBehavior: "continue"\n      });',
  1);

replace(
  '  if (isInvalid) {\n    const us2 = nextUs();',
  '  const dtsLo = pkt.dts ?? 0;\n  const dtsHi = pkt.dtshi ?? 0;\n  if (isInvalid && fallbackTimeBase && pkt.dts != null && !(dtsHi === -2147483648 && dtsLo === 0) && Number.isFinite(dtsLo)) {\n    const dtsUs = Math.round((dtsHi * 4294967296 + dtsLo) * 1e6 * fallbackTimeBase[0] / fallbackTimeBase[1]);\n    if (Number.isFinite(dtsUs)) {\n      pkt.pts = dtsUs;\n      pkt.ptshi = dtsUs < 0 ? -1 : 0;\n      pkt.time_base_num = 1;\n      pkt.time_base_den = 1e6;\n      return;\n    }\n  }\n  if (isInvalid) {\n    const us2 = nextUs();',
  1);

replace(
  '  return new VF(data, {\n    format,\n    codedWidth: frame.width,\n    codedHeight: frame.height,',
  '  ({ data, layout, transfer } = packPlanes(format, frame.width, frame.height, data, layout, transfer));\n' +
  '  return new VF(data, {\n    format,\n    codedWidth: frame.width,\n    codedHeight: frame.height,',
  1);

replace(
  'function laFrameToVideoFrame$1(frame, opts = {}) {',
  'function packPlanes(format, width, height, data, layout, transfer) {\n' +
  '  const halfWidth = Math.ceil(width / 2);\n' +
  '  const halfHeight = Math.ceil(height / 2);\n' +
  '  const planes = {\n' +
  '    I420: [[width, height], [halfWidth, halfHeight], [halfWidth, halfHeight]],\n' +
  '    I420A: [[width, height], [halfWidth, halfHeight], [halfWidth, halfHeight], [width, height]],\n' +
  '    I422: [[width, height], [halfWidth, height], [halfWidth, height]],\n' +
  '    NV12: [[width, height], [halfWidth * 2, halfHeight]],\n' +
  '    RGBA: [[width * 4, height]],\n' +
  '    BGRA: [[width * 4, height]]\n' +
  '  }[format];\n' +
  '  let expected = 0;\n' +
  '  const tight = Boolean(planes) && layout.length >= planes.length && planes.every(([rowBytes, rows], i) => {\n' +
  '    const ok = layout[i].stride === rowBytes && layout[i].offset === expected;\n' +
  '    expected += rowBytes * rows;\n' +
  '    return ok;\n' +
  '  });\n' +
  '  if (!planes || layout.length < planes.length || tight) {\n' +
  '    return { data, layout, transfer };\n' +
  '  }\n' +
  '  const source = data instanceof Uint8Array ? data : new Uint8Array(data.buffer ?? data, data.byteOffset ?? 0, data.byteLength);\n' +
  '  const packed = new Uint8Array(planes.reduce((sum, [rowBytes, rows]) => sum + rowBytes * rows, 0));\n' +
  '  const packedLayout = [];\n' +
  '  let offset = 0;\n' +
  '  planes.forEach(([rowBytes, rows], i) => {\n' +
  '    packedLayout.push({ offset, stride: rowBytes });\n' +
  '    for (let row = 0; row < rows; row++) {\n' +
  '      const start = layout[i].offset + row * layout[i].stride;\n' +
  '      packed.set(source.subarray(start, start + rowBytes), offset + row * rowBytes);\n' +
  '    }\n' +
  '    offset += rowBytes * rows;\n' +
  '  });\n' +
  '  return { data: packed, layout: packedLayout, transfer: [packed.buffer] };\n' +
  '}\n' +
  'function laFrameToVideoFrame$1(frame, opts = {}) {',
  1);

fs.writeFileSync(output, text);
console.log('patched');
