// 12S prismatic EV battery module. Geometry builds up with process stage:
// 0 cell stack (cells, end plates, straps) → 1 laser-welded busbars → 2 CMU board + harness
// → 3 inspected (vision mark) → 4 cover + laser-marked DMC; Good/Bad halo at the end.
import * as THREE from 'three';

export const MODULE = { length: 0.2, width: 0.11, height: 0.085 };

const shared = {};

function initShared() {
  if (shared.ready) return;
  const cellThickness = (MODULE.length - 0.014) / 12;
  shared.cellGeo = new THREE.BoxGeometry(cellThickness - 0.0008, 0.078, 0.104);
  shared.cellMat = new THREE.MeshPhysicalMaterial({ color: 0x1d4f9c, roughness: 0.35, metalness: 0.05, clearcoat: 0.5, clearcoatRoughness: 0.3 });
  shared.cellMatAlt = new THREE.MeshPhysicalMaterial({ color: 0x1a4688, roughness: 0.35, metalness: 0.05, clearcoat: 0.5, clearcoatRoughness: 0.3 });
  shared.terminalGeo = new THREE.BoxGeometry(cellThickness * 0.55, 0.004, 0.018);
  shared.terminalAl = new THREE.MeshStandardMaterial({ color: 0xd8dde3, metalness: 0.9, roughness: 0.3 });
  shared.terminalCu = new THREE.MeshStandardMaterial({ color: 0xc8794a, metalness: 0.9, roughness: 0.3 });
  shared.endPlateGeo = new THREE.BoxGeometry(0.006, 0.084, 0.11);
  shared.endPlateMat = new THREE.MeshStandardMaterial({ color: 0x1e2226, metalness: 0.4, roughness: 0.5 });
  shared.strapGeo = new THREE.BoxGeometry(MODULE.length, 0.012, 0.0015);
  shared.strapMat = new THREE.MeshStandardMaterial({ color: 0xb8bec6, metalness: 0.95, roughness: 0.25 });
  shared.busbarGeo = new THREE.BoxGeometry(cellThickness * 1.85, 0.0025, 0.016);
  shared.busbarMat = new THREE.MeshStandardMaterial({ color: 0xd9a066, metalness: 1.0, roughness: 0.22 });
  shared.weldGeo = new THREE.CylinderGeometry(0.0028, 0.0028, 0.0008, 10);
  shared.weldMat = new THREE.MeshStandardMaterial({ color: 0x6b4a2f, metalness: 0.7, roughness: 0.6 });
  shared.pcbGeo = new THREE.BoxGeometry(0.16, 0.0025, 0.036);
  shared.pcbMat = new THREE.MeshStandardMaterial({ color: 0x0f6b3a, roughness: 0.55, metalness: 0.1 });
  shared.chipGeo = new THREE.BoxGeometry(0.014, 0.003, 0.01);
  shared.chipMat = new THREE.MeshStandardMaterial({ color: 0x111317, roughness: 0.4 });
  shared.connGeo = new THREE.BoxGeometry(0.018, 0.008, 0.012);
  shared.connMat = new THREE.MeshStandardMaterial({ color: 0xf2f2f2, roughness: 0.5 });
  shared.flexGeo = new THREE.BoxGeometry(0.15, 0.0008, 0.012);
  shared.flexMat = new THREE.MeshStandardMaterial({ color: 0xd28a1d, roughness: 0.5, metalness: 0.2 });
  shared.markGeo = new THREE.BoxGeometry(0.012, 0.0008, 0.012);
  shared.markMat = new THREE.MeshStandardMaterial({ color: 0x1f8fff, emissive: 0x1f8fff, emissiveIntensity: 0.6 });
  shared.coverGeo = new THREE.BoxGeometry(MODULE.length + 0.006, 0.006, MODULE.width + 0.004);
  shared.coverMat = new THREE.MeshPhysicalMaterial({ color: 0x2b2f35, roughness: 0.5, clearcoat: 0.3 });
  shared.labelGeo = new THREE.PlaneGeometry(0.05, 0.03);
  shared.labelMat = new THREE.MeshStandardMaterial({ map: dmcTexture(), roughness: 0.6 });
  shared.haloGeo = new THREE.TorusGeometry(0.12, 0.004, 8, 48);
  shared.goodMat = new THREE.MeshStandardMaterial({ color: 0x052e16, emissive: 0x22c55e, emissiveIntensity: 4 });
  shared.badMat = new THREE.MeshStandardMaterial({ color: 0x3f0a0a, emissive: 0xef4444, emissiveIntensity: 4 });
  shared.suspectGeo = new THREE.SphereGeometry(0.006, 12, 8);
  shared.ready = true;
}

function dmcTexture() {
  const canvas = document.createElement('canvas');
  canvas.width = 160; canvas.height = 96;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#f4f4f0';
  ctx.fillRect(0, 0, 160, 96);
  ctx.fillStyle = '#111';
  let seed = 99;
  const rnd = () => ((seed = (seed * 48271) % 2147483647) / 2147483647);
  for (let y = 0; y < 14; y++) for (let x = 0; x < 14; x++) {
    if (x === 0 || y === 13 || rnd() > 0.52) ctx.fillRect(8 + x * 5, 10 + y * 5, 5, 5);
  }
  ctx.font = 'bold 13px monospace';
  ctx.fillText('EV-12S', 88, 30);
  ctx.font = '11px monospace';
  ctx.fillText('44.4V 52Ah', 88, 50);
  ctx.fillText('LINE 01', 88, 68);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

export function createBatteryModule() {
  initShared();
  const root = new THREE.Group();
  const s = shared;
  const L = MODULE.length;
  const cellThickness = (L - 0.014) / 12;

  const base = new THREE.Group();
  for (let i = 0; i < 12; i++) {
    const x = -L / 2 + 0.007 + cellThickness * (i + 0.5);
    const cell = new THREE.Mesh(s.cellGeo, i % 2 ? s.cellMat : s.cellMatAlt);
    cell.position.set(x, 0.039, 0);
    cell.castShadow = true;
    base.add(cell);
    // Alternating polarity for series connection
    const plus = new THREE.Mesh(s.terminalGeo, i % 2 ? s.terminalAl : s.terminalCu);
    plus.position.set(x, 0.08, 0.032);
    const minus = new THREE.Mesh(s.terminalGeo, i % 2 ? s.terminalCu : s.terminalAl);
    minus.position.set(x, 0.08, -0.032);
    base.add(plus, minus);
  }
  for (const sx of [-1, 1]) {
    const plate = new THREE.Mesh(s.endPlateGeo, s.endPlateMat);
    plate.position.set(sx * (L / 2 - 0.003), 0.042, 0);
    plate.castShadow = true;
    base.add(plate);
    const strap = new THREE.Mesh(s.strapGeo, s.strapMat);
    strap.position.set(0, 0.045, sx * 0.0558);
    base.add(strap);
  }
  root.add(base);

  const busbars = new THREE.Group();
  for (const side of [-1, 1]) {
    for (let i = side > 0 ? 0 : 1; i < 11; i += 2) {
      const x = -L / 2 + 0.007 + cellThickness * (i + 1);
      const bar = new THREE.Mesh(s.busbarGeo, s.busbarMat);
      bar.position.set(x, 0.0835, side * 0.032);
      busbars.add(bar);
      for (const dx of [-cellThickness * 0.5, cellThickness * 0.5]) {
        const weld = new THREE.Mesh(s.weldGeo, s.weldMat);
        weld.position.set(x + dx, 0.0852, side * 0.032);
        busbars.add(weld);
      }
    }
  }
  root.add(busbars);

  const cmu = new THREE.Group();
  const pcb = new THREE.Mesh(s.pcbGeo, s.pcbMat);
  pcb.position.set(0, 0.0875, 0);
  cmu.add(pcb);
  for (const [x, z] of [[-0.05, 0.006], [-0.02, -0.006], [0.015, 0.004], [0.045, -0.004]]) {
    const chip = new THREE.Mesh(s.chipGeo, s.chipMat);
    chip.position.set(x, 0.09, z);
    cmu.add(chip);
  }
  const conn = new THREE.Mesh(s.connGeo, s.connMat);
  conn.position.set(0.07, 0.092, 0);
  cmu.add(conn);
  for (const side of [-1, 1]) {
    const flex = new THREE.Mesh(s.flexGeo, s.flexMat);
    flex.position.set(0, 0.0858, side * 0.02);
    cmu.add(flex);
  }
  root.add(cmu);

  const inspected = new THREE.Mesh(s.markGeo, s.markMat);
  inspected.position.set(-0.08, 0.0895, 0.04);
  root.add(inspected);

  const finished = new THREE.Group();
  const cover = new THREE.Mesh(s.coverGeo, s.coverMat);
  cover.position.set(0, 0.0945, 0);
  cover.castShadow = true;
  finished.add(cover);
  const label = new THREE.Mesh(s.labelGeo, s.labelMat);
  label.rotation.x = -Math.PI / 2;
  label.position.set(0.05, 0.0981, 0.02);
  finished.add(label);
  root.add(finished);

  const halo = new THREE.Mesh(s.haloGeo, s.goodMat);
  halo.rotation.x = -Math.PI / 2;
  halo.position.y = 0.002;
  root.add(halo);

  const suspect = new THREE.Mesh(s.suspectGeo, s.badMat);
  suspect.position.set(0.09, 0.1, -0.045);
  root.add(suspect);

  root.userData = { busbars, cmu, inspected, finished, halo, suspect, stage: -1, status: '' };
  applyStage(root, 0, 'BaseLayer', false);
  return root;
}

export function applyStage(module, stage, status, defect) {
  const u = module.userData;
  if (u.stage === stage && u.status === status && u.defect === defect) return;
  u.busbars.visible = stage >= 1;
  u.cmu.visible = stage >= 2;
  u.inspected.visible = stage >= 3;
  u.finished.visible = stage >= 4;
  const final = status === 'Good' || status === 'Bad';
  u.halo.visible = final;
  u.halo.material = status === 'Bad' ? shared.badMat : shared.goodMat;
  u.suspect.visible = defect && !final;
  u.stage = stage;
  u.status = status;
  u.defect = defect;
}

/**
 * Keeps one 3D module per tracking number and parents it to wherever the frame says the part is:
 * a mover carrier, a robot gripper, a machine dial, an outfeed nest or the exit chute.
 */
export class PartManager {
  constructor(scene) {
    this.scene = scene;
    this.parts = new Map();
    this.exiting = [];
  }

  /** @param {Array<{trk:string, stage:number, status:string, defect:boolean, anchor:THREE.Object3D, yaw?:number}>} placements */
  update(placements, exitAnchors, dt) {
    const seen = new Set();
    for (const p of placements) {
      seen.add(p.trk);
      let entry = this.parts.get(p.trk);
      if (!entry) {
        entry = { mesh: createBatteryModule(), anchor: null };
        this.parts.set(p.trk, entry);
      }

      applyStage(entry.mesh, p.stage, p.status, p.defect);
      if (entry.anchor !== p.anchor) {
        p.anchor.add(entry.mesh);
        entry.anchor = p.anchor;
      }

      entry.mesh.position.set(0, 0, 0);
      entry.mesh.rotation.set(0, p.yaw ?? 0, 0);
      entry.lastStatus = p.status;
    }

    // Parts that left the line slide down the good / reject chute and disappear.
    for (const [trk, entry] of this.parts) {
      if (seen.has(trk)) continue;
      this.parts.delete(trk);
      const final = entry.lastStatus === 'Good' || entry.lastStatus === 'Bad';
      if (final && exitAnchors) {
        const chute = entry.lastStatus === 'Good' ? exitAnchors.good : exitAnchors.reject;
        chute.add(entry.mesh);
        entry.mesh.position.set(0, 0, 0.45);
        entry.mesh.rotation.set(0, Math.PI / 2, 0);
        this.exiting.push({ mesh: entry.mesh, t: 0 });
      } else {
        entry.mesh.removeFromParent();
      }
    }

    for (let i = this.exiting.length - 1; i >= 0; i--) {
      const e = this.exiting[i];
      e.t += dt;
      e.mesh.position.z = 0.45 - Math.min(1, e.t / 2.2) * 0.9;
      if (e.t > 2.6) {
        e.mesh.removeFromParent();
        this.exiting.splice(i, 1);
      }
    }
  }
}
