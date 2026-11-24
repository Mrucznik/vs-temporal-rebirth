// M5: the web configurator, drawn frame by frame in headless Chrome, so it is smooth however slow the computer is.
// node test/scenes/modpage_m5.mjs [out dir]   ->  M5.mp4 (1280x720, 30 fps) and M5.gif (800 px, 15 fps)
import { spawn, spawnSync } from 'child_process';
import fs from 'fs';
import os from 'os';
import path from 'path';

const page = new URL('../../docs/configurator/index.html', import.meta.url).href;
const out = process.argv[2] || path.join(os.homedir(), 'Videos/temporal-rebirth/modpage');
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'trt-m5-'));
const FPS = 30, W = 1280, H = 720;
fs.mkdirSync(out, { recursive: true });

const chrome = spawn('google-chrome', ['--headless=new', '--remote-debugging-port=0', '--user-data-dir=' + work + '/profile', '--no-first-run', '--lang=en', '--hide-scrollbars', 'about:blank'], { stdio: 'ignore' });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const cleanUp = () => { chrome.kill(); };
process.on('exit', cleanUp);

let ws;
for (let i = 0; i < 60 && !ws; i++) {
  try {
    const port = fs.readFileSync(work + '/profile/DevToolsActivePort', 'utf8').split('\n')[0];
    const tabs = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
    ws = new WebSocket(tabs.find((t) => t.type === 'page').webSocketDebuggerUrl);
  } catch { await sleep(250); }
}
await new Promise((r) => { ws.onopen = r; });
let id = 0;
const pending = {};
ws.onmessage = (m) => { const d = JSON.parse(m.data); if (d.id) { pending[d.id](d.result || { error: d.error }); delete pending[d.id]; } };
const send = (method, params = {}) => new Promise((r) => { pending[++id] = r; ws.send(JSON.stringify({ id, method, params })); });
const run = async (expression) => {
  const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error(expression + '\n' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
  return r.result.value;
};

await send('Page.enable');
await send('Runtime.enable');
await send('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: work + '/download' });
await send('Emulation.setFocusEmulationEnabled', { enabled: true });
await send('Emulation.setLocaleOverride', { locale: 'en_GB' });
await send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false });
await send('Page.addScriptToEvaluateOnNewDocument', { source: 'try { localStorage.setItem("tr.lang", "en") } catch {}' });
await send('Page.navigate', { url: page });
await sleep(1200);
await run('document.fonts.ready.then(() => 0)');
await run(`(() => {
  // Transitions and the toast run on the clock of the computer, frames do not.
  document.head.insertAdjacentHTML('beforeend', '<style>* { transition: none !important; scroll-behavior: auto !important } html { scrollbar-width: none }</style>');
  document.body.insertAdjacentHTML('beforeend', '<div id="rec" style="position:fixed;inset:0;z-index:99999;pointer-events:none;overflow:hidden">' +
    '<div id="recRing" style="position:absolute;width:44px;height:44px;border-radius:50%;border:3px solid #5fd4c4;box-sizing:border-box;opacity:0"></div>' +
    '<svg id="recCursor" width="30" height="36" viewBox="0 0 20 24" style="position:absolute;filter:drop-shadow(0 2px 3px rgba(0,0,0,.7))"><path d="M1,1 L1,18 L5.6,14 L8.6,21.4 L11.9,20 L8.9,12.9 L15,12.6 Z" fill="#fff" stroke="#111" stroke-width="1.3" stroke-linejoin="round"/></svg></div>');
  const later = window.setTimeout;
  window.setTimeout = (f, ms, ...rest) => later(f, ms === 2200 ? 600000 : ms, ...rest);
})()`);

const encoder = spawn('ffmpeg', ['-y', '-v', 'error', '-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'png', '-i', '-', '-c:v', 'libx264', '-preset', 'slow', '-crf', '16', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', out + '/M5.mp4'], { stdio: ['pipe', 'inherit', 'inherit'] });
let x = 900, y = 640, ring = 1, frames = 0;

async function frame() {
  // The cursor is part of the picture and the page gets the real mouse too, for its hover looks.
  await run(`(() => { const c = document.getElementById('recCursor').style, r = document.getElementById('recRing').style;
    c.left = '${x - 1.5}px'; c.top = '${y - 1.5}px'; r.left = '${x - 22}px'; r.top = '${y - 22}px'; r.opacity = ${ring < 1 ? 1 - ring : 0}; r.transform = 'scale(${0.35 + ring * 0.9})'; })()`);
  await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
  const shot = await send('Page.captureScreenshot', { format: 'png' });
  if (!encoder.stdin.write(Buffer.from(shot.data, 'base64'))) await new Promise((r) => encoder.stdin.once('drain', r));
  frames++;
  if (ring < 1) ring = Math.min(1, ring + 1 / (0.4 * FPS));
}

const ease = (t) => (t < 0.5 ? 4 * t ** 3 : 1 - (-2 * t + 2) ** 3 / 2);
const hold = async (s) => { for (let i = 0; i < Math.round(s * FPS); i++) await frame(); };
const place = (selector) => run(`(() => { const r = document.querySelector('${selector}').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()`);

async function moveTo(selector, s, dx = 0, dy = 0) {
  const [fx, fy] = [x, y], [tx, ty] = await place(selector), n = Math.round(s * FPS);
  for (let i = 1; i <= n; i++) {
    const t = ease(i / n);
    x = fx + (tx + dx - fx) * t;
    y = fy + (ty + dy - fy) * t - Math.sin(Math.PI * t) * 14;
    await frame();
  }
}

async function click() {
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount: 1 });
  ring = 0;
}

async function scrollTo(selector, top, s) {
  const from = await run('scrollY'), n = Math.round(s * FPS);
  const to = Math.max(0, Math.min(from + (await run(`document.querySelector('${selector}').getBoundingClientRect().top`)) - top, await run('document.documentElement.scrollHeight - innerHeight')));
  for (let i = 1; i <= n; i++) {
    await run(`scrollTo(0, ${from + (to - from) * ease(i / n)})`);
    await frame();
  }
}

const story = () => run(`[...document.querySelectorAll('#sentences dd')].map((d) => d.textContent).join(' | ')`);
console.log('language', await run('document.documentElement.lang'));

await hold(0.6);
await moveTo('#preset-Rift', 0.7, 90, 30);
await click();
await hold(0.7);
console.log('preset ', await story());
await scrollTo('#sec-time', 190, 1.1);
await hold(0.5);
await moveTo('#f-Time-Duration-d', 0.7, 0, 4);
await click();
await hold(0.3);
await moveTo('#f-Time-Duration-d', 0.4, 60, 90);
await hold(1.1);
console.log('changed', await story());
await scrollTo('#steps', 160, 1.0);
await moveTo('#download', 0.6, 0, 4);
await click();
await hold(1.4);
await sleep(500);
encoder.stdin.end();
await new Promise((r) => encoder.on('close', r));
console.log('downloaded', fs.existsSync(work + '/download') ? fs.readdirSync(work + '/download') : 'nothing', '|', frames, 'frames,', (frames / FPS).toFixed(1), 's');

spawnSync('ffmpeg', ['-y', '-v', 'error', '-i', out + '/M5.mp4', '-vf', 'fps=15,scale=800:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle', out + '/M5.gif'], { stdio: 'inherit' });
for (const f of ['M5.mp4', 'M5.gif']) console.log(path.join(out, f), (fs.statSync(path.join(out, f)).size / 1024).toFixed(0), 'kB');
chrome.kill();
await sleep(500);
fs.rmSync(work, { recursive: true, force: true });
process.exit(0);
