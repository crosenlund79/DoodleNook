// Mission 012: the expected frames for each fixture, as Chromium shows them. Chromium's WebCodecs ImageDecoder returns
// every frame of a GIF or animated PNG fully composed (offsets, disposal and blending applied the way the browser
// plays it). Writes chromium-<name>-NNN.rgba (raw straight RGBA, width × height × 4) plus one line per file in
// chromium-frames.txt: "<file> <frames> <width> <height>". Run: node make_expected_chromium.js (needs Playwright).
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  // ImageDecoder needs a secure context: an empty page on an https origin answered locally (no network).
  await page.route('https://fixtures.invalid/**', route => route.fulfill({ status: 200, contentType: 'text/html', body: '<html></html>' }));
  await page.goto('https://fixtures.invalid/');
  const files = fs.readdirSync(__dirname).filter(f => /^walk-.*\.(gif|png)$/.test(f)).sort();
  const index = [];
  for (const file of files) {
    const bytes = Array.from(fs.readFileSync(path.join(__dirname, file)));
    const type = file.endsWith('.gif') ? 'image/gif' : 'image/png';
    const frames = await page.evaluate(async ({ bytes, type }) => {
      const data = new Uint8Array(bytes);
      const decoder = new ImageDecoder({ data, type });
      await decoder.tracks.ready;
      const count = decoder.tracks.selectedTrack.frameCount;
      const out = [];
      for (let i = 0; i < count; i++) {
        const { image } = await decoder.decode({ frameIndex: i, completeFramesOnly: true });
        const w = image.displayWidth, h = image.displayHeight;
        const rgba = new Uint8Array(w * h * 4);
        await image.copyTo(rgba, { format: 'RGBA' });
        out.push({ w, h, rgba: Array.from(rgba) });
        image.close();
      }
      return out;
    }, { bytes, type });
    const name = file.replace(/\.(gif|png)$/, '');
    frames.forEach((f, i) => fs.writeFileSync(path.join(__dirname, `chromium-${name}-${String(i).padStart(3, '0')}.rgba`), Buffer.from(f.rgba)));
    index.push(`${file} ${frames.length} ${frames[0].w} ${frames[0].h}`);
    console.log(file, frames.length, frames[0].w, frames[0].h);
  }
  fs.writeFileSync(path.join(__dirname, 'chromium-frames.txt'), index.join('\n') + '\n');
  await browser.close();
})();
