// Factory hall: epoxy floor with safety markings, high-bay lighting, steel structure and background equipment.
import * as THREE from 'three';
import { MACHINE_ANGLES, CELL_OFFSET, offsetPoint, pathAt, yawOf } from './layout.js';

export const materials = {};

export function createMaterials() {
  const m = materials;
  m.aluminium = new THREE.MeshStandardMaterial({ color: 0xc9ced6, metalness: 0.9, roughness: 0.32 });
  m.aluminiumDark = new THREE.MeshStandardMaterial({ color: 0x8a919b, metalness: 0.85, roughness: 0.4 });
  m.aluminiumMatte = new THREE.MeshStandardMaterial({ color: 0xa9afb7, metalness: 0.75, roughness: 0.62 }); // bead-blasted plates
  m.anodizedBlack = new THREE.MeshStandardMaterial({ color: 0x15171a, metalness: 0.6, roughness: 0.45 });
  m.steel = new THREE.MeshStandardMaterial({ color: 0x9aa3ad, metalness: 0.95, roughness: 0.25 });
  m.cabinet = new THREE.MeshPhysicalMaterial({ color: 0xaeb3b0, metalness: 0.05, roughness: 0.62, clearcoat: 0.25, clearcoatRoughness: 0.5 });
  m.cabinetDark = new THREE.MeshStandardMaterial({ color: 0x2d3238, metalness: 0.3, roughness: 0.6 });
  m.beckhoffOrange = new THREE.MeshStandardMaterial({ color: 0xff6a00, metalness: 0.2, roughness: 0.45 });
  m.robotOrange = new THREE.MeshPhysicalMaterial({ color: 0xff7a1a, metalness: 0.1, roughness: 0.38, clearcoat: 0.6, clearcoatRoughness: 0.25 });
  m.robotGrey = new THREE.MeshStandardMaterial({ color: 0x3b4048, metalness: 0.5, roughness: 0.45 });
  m.glass = new THREE.MeshPhysicalMaterial({ color: 0xbfd8ff, metalness: 0, roughness: 0.3, transparent: true, opacity: 0.09, depthWrite: false, side: THREE.DoubleSide, envMapIntensity: 0.25, specularIntensity: 0.4 });
  m.yellowPaint = new THREE.MeshStandardMaterial({ color: 0xf2c200, metalness: 0.1, roughness: 0.6 });
  m.rubber = new THREE.MeshStandardMaterial({ color: 0x111111, roughness: 0.9 });
  m.screen = new THREE.MeshStandardMaterial({ color: 0x0a0f14, roughness: 0.25, metalness: 0.2 });
  m.ledCyan = new THREE.MeshStandardMaterial({ color: 0x002a33, emissive: 0x18d6ff, emissiveIntensity: 3.2 });
  return m;
}

function concreteTexture() {
  const size = 1024;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#5b6066';
  ctx.fillRect(0, 0, size, size);
  // Deterministic speckle (seeded LCG) so captures are reproducible.
  let seed = 1234567;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  for (let i = 0; i < 26000; i++) {
    const v = 70 + rnd() * 45;
    ctx.fillStyle = `rgba(${v},${v + 3},${v + 6},${0.08 + rnd() * 0.12})`;
    const r = rnd() * 2.2;
    ctx.fillRect(rnd() * size, rnd() * size, r, r);
  }
  for (let i = 0; i < 40; i++) {
    ctx.strokeStyle = `rgba(30,32,36,${0.05 + rnd() * 0.06})`;
    ctx.lineWidth = 1 + rnd() * 2;
    ctx.beginPath();
    let x = rnd() * size, y = rnd() * size;
    ctx.moveTo(x, y);
    for (let k = 0; k < 6; k++) { x += (rnd() - 0.5) * 120; y += (rnd() - 0.5) * 120; ctx.lineTo(x, y); }
    ctx.stroke();
  }
  // Expansion joints (6 m slabs at 4 m texture repeat)
  ctx.strokeStyle = 'rgba(25,27,30,0.55)';
  ctx.lineWidth = 3;
  ctx.strokeRect(0, 0, size, size);
  const tex = new THREE.CanvasTexture(canvas);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(10, 10);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 8;
  return tex;
}

function stripeTexture() {
  const canvas = document.createElement('canvas');
  canvas.width = 128; canvas.height = 16;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#f2c200';
  ctx.fillRect(0, 0, 128, 16);
  ctx.fillStyle = '#151515';
  for (let x = -16; x < 128; x += 32) {
    ctx.beginPath();
    ctx.moveTo(x, 16); ctx.lineTo(x + 16, 0); ctx.lineTo(x + 32, 0); ctx.lineTo(x + 16, 16);
    ctx.fill();
  }
  const tex = new THREE.CanvasTexture(canvas);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

function addFloorLine(group, x1, z1, x2, z2, width, material) {
  const len = Math.hypot(x2 - x1, z2 - z1);
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(len, width), material);
  mesh.rotation.x = -Math.PI / 2;
  mesh.rotation.z = Math.atan2(-(z2 - z1), x2 - x1);
  mesh.position.set((x1 + x2) / 2, 0.003, (z1 + z2) / 2);
  mesh.receiveShadow = true;
  group.add(mesh);
}

function addHazardFrame(group, cx, cz, w, d, yaw, stripes) {
  const frame = new THREE.Group();
  const mat = new THREE.MeshStandardMaterial({ map: stripes.clone(), roughness: 0.7 });
  mat.map.repeat.set(w * 6, 1);
  mat.map.needsUpdate = true;
  const matD = new THREE.MeshStandardMaterial({ map: stripes.clone(), roughness: 0.7 });
  matD.map.repeat.set(d * 6, 1);
  matD.map.needsUpdate = true;
  const t = 0.05;
  addFloorLine(frame, -w / 2, -d / 2, w / 2, -d / 2, t, mat);
  addFloorLine(frame, -w / 2, d / 2, w / 2, d / 2, t, mat);
  addFloorLine(frame, -w / 2, -d / 2, -w / 2, d / 2, t, matD);
  addFloorLine(frame, w / 2, -d / 2, w / 2, d / 2, t, matD);
  frame.position.set(cx, 0, cz);
  frame.rotation.y = yaw;
  group.add(frame);
}

export function buildFactory(scene) {
  const group = new THREE.Group();
  group.name = 'factory';

  // Epoxy-coated concrete floor
  const floorMat = new THREE.MeshStandardMaterial({ map: concreteTexture(), roughness: 0.38, metalness: 0.08, color: 0x5a6068 });
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(60, 60), floorMat);
  floor.rotation.x = -Math.PI / 2;
  floor.receiveShadow = true;
  group.add(floor);

  // Walkways (green) around the line, yellow aisle borders
  const walkway = new THREE.MeshStandardMaterial({ color: 0x1f4a37, roughness: 0.6, metalness: 0.0 });
  const aisle = new THREE.Mesh(new THREE.PlaneGeometry(14, 1.6), walkway);
  aisle.rotation.x = -Math.PI / 2;
  aisle.position.set(0, 0.002, 4.2);
  aisle.receiveShadow = true;
  group.add(aisle);
  const yellow = new THREE.MeshStandardMaterial({ color: 0xb38f00, roughness: 0.7 });
  addFloorLine(group, -7, 3.38, 7, 3.38, 0.08, yellow);
  addFloorLine(group, -7, 5.02, 7, 5.02, 0.08, yellow);

  // Hazard zones around every robot cell
  const stripes = stripeTexture();
  MACHINE_ANGLES.forEach((deg) => {
    const p = pathAt(deg);
    const c = offsetPoint(deg, CELL_OFFSET * 0.78);
    addHazardFrame(group, c.x, c.z, 1.9, 1.9, yawOf(p), stripes);
  });

  // Line safety perimeter
  addHazardFrame(group, 0, 0, 6.6, 5.4, 0, stripes);

  // Steel columns and roof structure
  const columnMat = new THREE.MeshStandardMaterial({ color: 0x48505a, metalness: 0.7, roughness: 0.5 });
  const footMat = new THREE.MeshStandardMaterial({ color: 0xf2c200, roughness: 0.6 });
  const colGeo = new THREE.BoxGeometry(0.32, 9, 0.32);
  const footGeo = new THREE.BoxGeometry(0.42, 0.9, 0.42);
  for (const x of [-9, 0, 9]) {
    for (const z of [-7.5, 7.5]) {
      const col = new THREE.Mesh(colGeo, columnMat);
      col.position.set(x, 4.5, z);
      col.castShadow = true;
      group.add(col);
      const foot = new THREE.Mesh(footGeo, footMat);
      foot.position.set(x, 0.45, z);
      group.add(foot);
    }
  }

  // Overhead high-bay LED panels (emissive) + cable trays
  const panelMat = new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: 0xeaf2ff, emissiveIntensity: 3.0 });
  const panelGeo = new THREE.BoxGeometry(1.6, 0.04, 0.35);
  for (const x of [-4.5, -1.5, 1.5, 4.5]) {
    for (const z of [-3.2, 0, 3.2]) {
      const panel = new THREE.Mesh(panelGeo, panelMat);
      panel.position.set(x, 6.2, z);
      group.add(panel);
    }
  }

  const trayMat = new THREE.MeshStandardMaterial({ color: 0x7d858f, metalness: 0.8, roughness: 0.4 });
  const tray = new THREE.Mesh(new THREE.BoxGeometry(18, 0.08, 0.4), trayMat);
  tray.position.set(0, 4.6, -3.9);
  group.add(tray);
  const tray2 = tray.clone();
  tray2.position.z = 3.9;
  group.add(tray2);

  // Back wall with line signage
  const wallMat = new THREE.MeshStandardMaterial({ color: 0x2a3038, roughness: 0.9 });
  const wall = new THREE.Mesh(new THREE.PlaneGeometry(40, 10), wallMat);
  wall.position.set(0, 5, -9);
  wall.receiveShadow = true;
  group.add(wall);
  group.add(makeSign('LINE 01 · EV BATTERY MODULE ASSEMBLY', 0, 4.4, -8.95));

  // Background racks with module trays (adds depth to the hall)
  const rackMat = new THREE.MeshStandardMaterial({ color: 0x1f5fa8, metalness: 0.4, roughness: 0.5 });
  const boxMat = new THREE.MeshStandardMaterial({ color: 0x6b5a45, roughness: 0.85 });
  for (let i = 0; i < 6; i++) {
    const rack = new THREE.Group();
    for (const y of [0.05, 1.05, 2.05]) {
      const shelf = new THREE.Mesh(new THREE.BoxGeometry(2.4, 0.06, 0.9), rackMat);
      shelf.position.y = y;
      rack.add(shelf);
      for (let k = 0; k < 3; k++) {
        const box = new THREE.Mesh(new THREE.BoxGeometry(0.6, 0.45, 0.6), boxMat);
        box.position.set(-0.75 + k * 0.75, y + 0.26, 0);
        box.castShadow = true;
        rack.add(box);
      }
    }
    for (const sx of [-1.18, 1.18]) {
      for (const sz of [-0.42, 0.42]) {
        const post = new THREE.Mesh(new THREE.BoxGeometry(0.06, 2.6, 0.06), rackMat);
        post.position.set(sx, 1.3, sz);
        rack.add(post);
      }
    }
    rack.position.set(-7.5 + i * 3, 0, -7.6);
    group.add(rack);
  }

  scene.add(group);
  return group;
}

function makeSign(text, x, y, z) {
  const canvas = document.createElement('canvas');
  canvas.width = 2048; canvas.height = 160;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#11161c';
  ctx.fillRect(0, 0, 2048, 160);
  ctx.fillStyle = '#ff7a1a';
  ctx.fillRect(0, 0, 18, 160);
  ctx.font = '600 84px "Segoe UI", Inter, sans-serif';
  ctx.fillStyle = '#e8eef5';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, 60, 84);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(12.8, 1.0), new THREE.MeshStandardMaterial({ map: tex, emissive: 0xffffff, emissiveMap: tex, emissiveIntensity: 0.35, roughness: 0.6 }));
  mesh.position.set(x, y, z);
  return mesh;
}

export function buildLights(scene) {
  const hemi = new THREE.HemisphereLight(0xcfe0ff, 0x1c1a17, 0.42);
  scene.add(hemi);

  const key = new THREE.DirectionalLight(0xfff3e2, 2.0);
  key.position.set(4.5, 9, 5.5);
  key.castShadow = true;
  key.shadow.mapSize.set(2048, 2048);
  key.shadow.camera.left = -6;
  key.shadow.camera.right = 6;
  key.shadow.camera.top = 6;
  key.shadow.camera.bottom = -6;
  key.shadow.camera.near = 1;
  key.shadow.camera.far = 25;
  key.shadow.bias = -0.0004;
  key.shadow.normalBias = 0.02;
  scene.add(key);

  const fill = new THREE.DirectionalLight(0xbcd4ff, 0.7);
  fill.position.set(-6, 5, -4);
  scene.add(fill);

  const rim = new THREE.SpotLight(0x9fd0ff, 6, 14, Math.PI / 5, 0.6, 1.4);
  rim.position.set(-3, 6, 4);
  rim.target.position.set(0, 0.9, 0);
  scene.add(rim, rim.target);
  return { hemi, key, fill, rim };
}
