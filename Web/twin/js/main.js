// XTS line digital twin – bootstrap, render loop and data plumbing.
//   index.html                     → live from the WPF host (WebView2) or demo replay
//   index.html?replay=frames.json  → replay frames recorded from the real engine
//   &tour=overview|stations|ai     → start a cinematic tour
//   &capture=1                     → deterministic frame-by-frame rendering for video export
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { CSS2DRenderer } from 'three/addons/renderers/CSS2DRenderer.js';

import { createMaterials, buildFactory, buildLights, createFactoryEnvironment } from './factory.js';
import { buildTrack, MoverFleet, getEntryAnchors, getExitAnchors } from './track.js';
import { buildCells } from './cells.js';
import { PartManager, createBatteryModule, applyStage } from './parts.js';
import { CameraDirector, overviewPose } from './camera.js';
import { Hud } from './hud.js';
import { LiveSource, ReplaySource, loadFrames } from './data.js';

const params = new URLSearchParams(location.search);
const capture = params.get('capture') === '1';
if (capture) document.body.classList.add('capture');

// ------------------------------------------------------------------ renderer + scene
const container = document.getElementById('viewport');
const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: capture, powerPreference: 'high-performance' });
renderer.setPixelRatio(capture ? 1 : Math.min(window.devicePixelRatio, 2));
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.0;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFShadowMap;
container.appendChild(renderer.domElement);

const labelRenderer = new CSS2DRenderer();
labelRenderer.setSize(window.innerWidth, window.innerHeight);
labelRenderer.domElement.style.position = 'fixed';
labelRenderer.domElement.style.inset = '0';
labelRenderer.domElement.style.pointerEvents = 'none';
container.appendChild(labelRenderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0c1016);
scene.fog = new THREE.Fog(0x0c1016, 12, 30);
const pmrem = new THREE.PMREMGenerator(renderer);
scene.environment = pmrem.fromScene(createFactoryEnvironment(), 0.03).texture;
scene.environmentIntensity = 1.0;

const camera = new THREE.PerspectiveCamera(42, window.innerWidth / window.innerHeight, 0.03, 120);
const start = overviewPose();
camera.position.copy(start.pos);

const controls = new OrbitControls(camera, renderer.domElement);
controls.target.copy(start.target);
controls.enableDamping = true;
controls.dampingFactor = 0.08;
controls.minDistance = 0.4;
controls.maxDistance = 16;
controls.maxPolarAngle = Math.PI * 0.49;
controls.update();

const composer = new EffectComposer(renderer);
composer.addPass(new RenderPass(scene, camera));
// Bloom only for true emitters (LEDs, andon, laser): lit surfaces stay below the HDR threshold.
const bloom = new UnrealBloomPass(new THREE.Vector2(window.innerWidth, window.innerHeight), 0.75, 0.45, 1.6);
composer.addPass(bloom);
composer.addPass(new OutputPass());

// ------------------------------------------------------------------ world
createMaterials();
buildFactory(scene);
buildLights(scene);
buildTrack(scene);
const fleet = new MoverFleet(scene, 10);
const cells = buildCells(scene, null);
const parts = new PartManager(scene);
const director = new CameraDirector(camera, controls);
const hud = new Hud();

// Infeed: two cell stacks waiting on the conveyor + the stack in the gantry gripper
const entry = getEntryAnchors();
const waiting = [createBatteryModule(), createBatteryModule()];
waiting.forEach((m, i) => { m.position.set(0, 0, -0.3 * i); entry.stack.add(m); });
const infeedModule = createBatteryModule();
infeedModule.position.set(0, -0.53, 0);
infeedModule.visible = false;
entry.gripper.add(infeedModule);
let entryDockTime = 0;

const helpers = {
  pickLoadedMover() {
    const m = lastFrame?.movers.find((x) => x.part && x.part.stage <= 1) ?? lastFrame?.movers.find((x) => x.part);
    return m ? m.id : 0;
  },
  busiestCell() {
    const busy = lastFrame?.robots.findIndex((r) => r.st !== 'Idle');
    return busy >= 0 ? lastFrame.robots[busy].m : 1;
  }
};

let lastFrame = null;

function applyFrame(frame, dt) {
  if (!frame) return;
  lastFrame = frame;
  fleet.update(frame, camera.position.length() < 6.5 && !capture);

  const robotByMachine = new Map(frame.robots.map((r) => [r.m, r]));
  cells.forEach((cell, i) => cell.update(frame.machines[i], robotByMachine.get(frame.machines[i]?.id), dt));

  // Where is every module right now?
  const placements = [];
  for (const m of frame.movers) {
    if (m.part) placements.push({ ...m.part, trk: m.part.trk, anchor: fleet.carrierOf(m.id) });
  }
  for (const r of frame.robots) {
    const cell = cells[r.m];
    if (!cell) continue;
    if (r.part) placements.push({ ...r.part, anchor: cell.robot.tcpA });
    if (r.part2) placements.push({ ...r.part2, anchor: cell.robot.tcpB });
  }
  frame.machines.forEach((m, i) => {
    if (m.part) placements.push({ ...m.part, anchor: cells[i].partAnchor });
    if (m.nest) placements.push({ ...m.nest, anchor: cells[i].nestAnchor });
  });
  parts.update(placements, getExitAnchors(), dt);

  // Infeed gantry: lower a fresh cell stack while a mover is docked at the entry
  const atEntry = frame.movers.find((m) => m.dock === -2);
  entryDockTime = atEntry ? entryDockTime + dt : 0;
  const k = Math.min(1, entryDockTime / 0.9);
  entry.gripper.position.y = entry.gripper.userData.base.y - 0.36 * Math.sin(Math.PI * Math.min(1, k)) * (atEntry ? 1 : 0);
  infeedModule.visible = !!atEntry && !atEntry.part && k < 0.98;
  applyStage(infeedModule, 0, 'BaseLayer', false);

  hud.update(frame);

  // Labels: compact when far away so the overview stays readable
  const far = camera.position.distanceTo(controls.target) > 3.2;
  cells.forEach((cell) => cell.labelEl.classList.toggle('compact', far));
}

function render() {
  composer.render();
  labelRenderer.render(scene, camera);
}

function onResize() {
  const w = window.innerWidth, h = window.innerHeight;
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  renderer.setSize(w, h);
  composer.setSize(w, h);
  bloom.setSize(w, h);
  labelRenderer.setSize(w, h);
}
window.addEventListener('resize', onResize);

// ------------------------------------------------------------------ UI: camera buttons
const buttons = [...document.querySelectorAll('nav button')];
function selectCamera(name) {
  buttons.forEach((b) => b.classList.toggle('active', b.dataset.cam === name));
  if (name === 'tour') director.startTour(params.get('tour') ?? 'overview');
  else { director.setPreset(name, fleet); hud.caption(null); }
}
buttons.forEach((b) => b.addEventListener('click', () => selectCamera(b.dataset.cam)));
renderer.domElement.addEventListener('pointerdown', () => {
  if (director.mode === 'tour' || director.mode === 'follow') {
    director.setPreset('free', fleet);
    director.desired = null;
    controls.enabled = true;
    buttons.forEach((b) => b.classList.remove('active'));
    hud.caption(null);
  }
});

// ------------------------------------------------------------------ data source + loop
async function boot() {
  let source;
  if (window.chrome?.webview && !params.get('replay')) {
    source = new LiveSource();
    source.onCommand((msg) => {
      if (msg.type === 'camera') selectCamera(msg.preset);
      if (msg.type === 'tour') { params.set('tour', msg.name ?? 'overview'); selectCamera('tour'); }
    });
    hud.setMode('LIVE');
  } else {
    const url = params.get('replay') ?? 'demo-frames.json';
    const frames = await loadFrames(url);
    const fps = Number(params.get('fps') ?? (params.get('replay') ? 30 : 10));
    source = new ReplaySource(frames, fps, !capture, params.get('replay') ? 'REPLAY' : 'DEMO');
    hud.setMode(source.mode);
  }

  if (capture) {
    setupCapture(source);
    return;
  }

  if (params.get('tour')) selectCamera('tour');
  hud.hideLoading();

  let last = performance.now();
  const loop = (now) => {
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    if (source.advance) source.advance(dt);
    const frame = source.sample(now);
    applyFrame(frame, dt);
    const caption = director.update(dt, fleet, lastFrame, helpers);
    if (director.mode === 'tour') hud.caption(caption);
    render();
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
}

/** Deterministic frame-by-frame rendering for the video pipeline (Playwright + ffmpeg). */
function setupCapture(source) {
  const fps = source.fps;
  const tourName = params.get('tour') ?? 'overview';
  director.startTour(tourName);
  hud.hideLoading();
  // Warm-up: settle robot IK / dial smoothing on the first frame.
  for (let i = 0; i < 30; i++) applyFrame(source.frameAt(0), 1 / fps);

  window.twinCapture = {
    frameCount: source.frames.length,
    fps,
    tourDuration: director.tour.duration,
    renderFrame(index) {
      const frame = source.frameAt(index);
      applyFrame(frame, 1 / fps);
      const caption = director.applyTour(index / fps, fleet, lastFrame, helpers);
      hud.caption(caption);
      render();
      return true;
    }
  };
  window.twinReady = true;
}

boot().catch((err) => {
  console.error(err);
  document.getElementById('loading').textContent = `Twin failed to start: ${err.message}`;
});
