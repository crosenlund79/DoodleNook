const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
  for (const n of ['A_pure_sketch','B_quick_sketch','C_painting','D_editing','E_pixel_art']) {
    await p.goto('file://' + __dirname + '/' + n + '.html');
    await p.screenshot({ path: __dirname + '/' + n + '.png' });
  }
  await b.close();
})();
