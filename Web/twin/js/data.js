// Frame sources for the twin:
//  - LIVE:   WPF host pushes TwinFrames through WebView2 (window.chrome.webview)
//  - REPLAY: frames recorded headlessly from the real engine (tools/TwinRecorder)
// Between frames the twin interpolates so motion stays smooth at 60 fps.
import { lerp, lerpAngle } from './layout.js';

export function interpolateFrames(a, b, t) {
  if (!a) return b;
  if (!b || t <= 0) return a;
  if (t >= 1) return b;
  const base = t < 0.5 ? a : b;
  const moverById = new Map(b.movers.map((m) => [m.id, m]));
  const movers = a.movers.map((ma) => {
    const mb = moverById.get(ma.id);
    if (!mb) return ma;
    return { ...(t < 0.5 ? ma : mb), pos: lerpAngle(ma.pos, mb.pos, t), vel: lerp(ma.vel, mb.vel, t) };
  });
  const robotsB = new Map(b.robots.map((r) => [r.id, r]));
  const robots = a.robots.map((ra) => {
    const rb = robotsB.get(ra.id);
    if (!rb) return ra;
    if (ra.st === rb.st && rb.p >= ra.p) return { ...(t < 0.5 ? ra : rb), p: lerp(ra.p, rb.p, t) };
    return t < 0.5 ? ra : rb;
  });
  const machinesB = new Map(b.machines.map((m) => [m.id, m]));
  const machines = a.machines.map((ma) => {
    const mb = machinesB.get(ma.id);
    if (!mb) return ma;
    const src = t < 0.5 ? ma : mb;
    const stations = src.stations.map((s, i) => {
      const sa = ma.stations[i], sb = mb.stations[i];
      return sa && sb && sa.st === sb.st && sb.p >= sa.p ? { ...s, p: lerp(sa.p, sb.p, t) } : s;
    });
    return { ...src, stations };
  });
  return { ...base, t: lerp(a.t, b.t, t), movers, robots, machines };
}

export class LiveSource {
  constructor() {
    this.mode = 'LIVE';
    this.buffer = [];
    this.handlers = [];
    window.chrome.webview.addEventListener('message', (e) => {
      const msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
      if (msg?.type && msg.type !== 'frame') {
        this.handlers.forEach((h) => h(msg));
        return;
      }

      const frame = msg?.frame ?? msg;
      if (!frame?.movers) return;
      this.buffer.push({ frame, at: performance.now() });
      if (this.buffer.length > 4) this.buffer.shift();
    });
    window.chrome.webview.postMessage({ type: 'ready' });
  }

  onCommand(handler) { this.handlers.push(handler); }

  get ready() { return this.buffer.length > 0; }

  /** Render ~100 ms behind the newest frame and interpolate between the bracketing pair. */
  sample(now) {
    const b = this.buffer;
    if (b.length === 0) return null;
    if (b.length === 1) return b[0].frame;
    const renderAt = now - 100;
    for (let i = b.length - 1; i > 0; i--) {
      if (b[i - 1].at <= renderAt) {
        const span = Math.max(1, b[i].at - b[i - 1].at);
        return interpolateFrames(b[i - 1].frame, b[i].frame, (renderAt - b[i - 1].at) / span);
      }
    }
    return b[0].frame;
  }
}

export class ReplaySource {
  constructor(frames, fps, loop, mode = 'REPLAY') {
    this.frames = frames;
    this.fps = fps;
    this.loop = loop;
    this.mode = mode;
    this.time = 0;
  }

  get ready() { return this.frames.length > 0; }
  get duration() { return this.frames.length / this.fps; }

  advance(dt) {
    this.time += dt;
    if (this.loop && this.time >= this.duration) this.time %= this.duration;
  }

  sampleAt(time) {
    const f = Math.max(0, Math.min(this.frames.length - 1, time * this.fps));
    const i = Math.floor(f);
    const next = Math.min(this.frames.length - 1, i + 1);
    return interpolateFrames(this.frames[i], this.frames[next], f - i);
  }

  sample() { return this.sampleAt(this.time); }

  frameAt(index) { return this.frames[Math.max(0, Math.min(this.frames.length - 1, index))]; }
}

export async function loadFrames(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`Could not load ${url}: ${response.status}`);
  return response.json();
}
