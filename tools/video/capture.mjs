// Deterministic video capture of the 3D digital twin.
//   node capture.mjs --frames <frames.json> --tour overview|stations|ai --out <dir>
//                    [--width 1280 --height 720 --start 0 --count N --stride 1 --chromium <path>]
// Serves Web/twin over HTTP, opens it in headless Chromium (WebGL via SwiftShader), calls
// window.twinCapture.renderFrame(i) for each recorded frame and saves PNGs for ffmpeg.
import { chromium } from 'playwright-core';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => {
  if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1] && !all[i + 1].startsWith('--') ? all[i + 1] : 'true']);
  return acc;
}, []));

const here = path.dirname(fileURLToPath(import.meta.url));
const twinRoot = path.resolve(here, '../../Web/twin');
const framesPath = path.resolve(args.frames ?? 'frames.json');
const outDir = path.resolve(args.out ?? 'out');
const width = Number(args.width ?? 1280);
const height = Number(args.height ?? 720);
const tour = args.tour ?? 'overview';
const stride = Number(args.stride ?? 1);
const chromiumPath = args.chromium ?? process.env.CHROMIUM_PATH ?? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome';
fs.mkdirSync(outDir, { recursive: true });

const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.txt': 'text/plain' };
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url === '/__frames.json' ? framesPath : path.join(twinRoot, url === '/' ? 'index.html' : url);
  if (!file.startsWith(twinRoot) && file !== framesPath) { res.writeHead(403); res.end(); return; }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream' });
    res.end(data);
  });
}).listen(0);
const port = server.address().port;

const browser = await chromium.launch({
  executablePath: chromiumPath,
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--disable-gpu-vsync']
});
const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') console.log(`[page ${m.type()}] ${m.text()}`); });
page.on('pageerror', (e) => console.log(`[page error] ${e.message}`));

await page.goto(`http://localhost:${port}/index.html?replay=/__frames.json&capture=1&tour=${tour}&fps=${args.fps ?? 30}`);
await page.waitForFunction(() => window.twinReady === true, null, { timeout: 120000 });
const total = await page.evaluate(() => window.twinCapture.frameCount);
const startFrame = Number(args.start ?? 0);
const count = Math.min(Number(args.count ?? total), total - startFrame);
console.log(`Rendering ${count} of ${total} frames (${width}x${height}, tour ${tour}) → ${outDir}`);

const t0 = Date.now();
let n = 0;
for (let i = startFrame; i < startFrame + count; i += stride) {
  await page.evaluate((idx) => window.twinCapture.renderFrame(idx), i);
  await page.screenshot({ path: path.join(outDir, `frame_${String(n).padStart(5, '0')}.png`), type: 'png' });
  n++;
  if (n % 60 === 0) {
    const rate = n / ((Date.now() - t0) / 1000);
    console.log(`  ${n}/${Math.ceil(count / stride)} frames · ${rate.toFixed(1)} fps`);
  }
}

await browser.close();
server.close();
console.log(`Done: ${n} frames in ${((Date.now() - t0) / 1000).toFixed(0)} s`);
