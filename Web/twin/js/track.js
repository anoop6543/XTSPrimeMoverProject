// XTS linear transport: motor modules on a machine table, guide rails, module LEDs, movers with rollers.
import * as THREE from 'three';
import { CSS2DObject } from 'three/addons/renderers/CSS2DRenderer.js';
import { materials } from './factory.js';
import {
  RADIUS, STRAIGHT, TRACK_LENGTH, TRACK_TOP, TRACK_HALF_WIDTH, ENTRY_ANGLE, EXIT_ANGLE,
  pathAt, yawOf, offsetPoint, stadiumShape, stadiumPath
} from './layout.js';

const MODULE_BOTTOM = 0.80;
const MODULE_HEIGHT = 0.115;
const TABLE_TOP = 0.78;

function ringGeometry(inner, outer, depth) {
  const shape = stadiumShape(outer);
  shape.holes.push(stadiumPath(inner));
  const geo = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: false, curveSegments: 48 });
  geo.rotateX(-Math.PI / 2);
  return geo;
}

export function buildTrack(scene) {
  const group = new THREE.Group();
  group.name = 'xts-track';

  // Machine table (stadium top, aluminium edge, levelling feet)
  const top = new THREE.Mesh(
    new THREE.ExtrudeGeometry(stadiumShape(RADIUS + 0.22), { depth: 0.035, bevelEnabled: true, bevelSize: 0.006, bevelThickness: 0.006, bevelSegments: 2, curveSegments: 48 }),
    materials.cabinetDark);
  top.geometry.rotateX(-Math.PI / 2);
  top.position.y = TABLE_TOP - 0.035;
  top.castShadow = top.receiveShadow = true;
  group.add(top);

  const edge = new THREE.Mesh(ringGeometry(RADIUS + 0.2, RADIUS + 0.235, 0.05), materials.aluminium);
  edge.position.y = TABLE_TOP - 0.06;
  group.add(edge);

  const legGeo = new THREE.BoxGeometry(0.06, TABLE_TOP - 0.06, 0.06);
  const footGeo = new THREE.CylinderGeometry(0.035, 0.04, 0.03, 16);
  const legSpots = [];
  for (const x of [-STRAIGHT / 2, 0, STRAIGHT / 2]) {
    for (const z of [-(RADIUS + 0.12), RADIUS + 0.12]) legSpots.push([x, z]);
  }
  legSpots.push([-(STRAIGHT / 2 + RADIUS + 0.1), 0], [STRAIGHT / 2 + RADIUS + 0.1, 0]);
  for (const [x, z] of legSpots) {
    const leg = new THREE.Mesh(legGeo, materials.aluminiumDark);
    leg.position.set(x, (TABLE_TOP - 0.06) / 2, z);
    leg.castShadow = true;
    group.add(leg);
    const foot = new THREE.Mesh(footGeo, materials.steel);
    foot.position.set(x, 0.015, z);
    group.add(foot);
  }

  // Centre: TwinCAT industrial PC + EtherCAT junction on the table
  const ipc = new THREE.Mesh(new THREE.BoxGeometry(0.34, 0.2, 0.22), materials.anodizedBlack);
  ipc.position.set(0, TABLE_TOP + 0.1, 0);
  ipc.castShadow = true;
  group.add(ipc);
  const ipcFront = new THREE.Mesh(new THREE.PlaneGeometry(0.3, 0.16), new THREE.MeshStandardMaterial({ color: 0x0b0e12, emissive: 0x1a6fff, emissiveIntensity: 0.25, roughness: 0.3 }));
  ipcFront.position.set(0, TABLE_TOP + 0.1, 0.111);
  group.add(ipcFront);
  for (let i = 0; i < 4; i++) {
    const led = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.006, 0.004), materials.ledCyan);
    led.position.set(-0.12 + i * 0.02, TABLE_TOP + 0.17, 0.112);
    group.add(led);
  }

  // Motor modules: black anodised body, steel guide rails on top and bottom of the outer face
  const body = new THREE.Mesh(ringGeometry(RADIUS - TRACK_HALF_WIDTH, RADIUS + TRACK_HALF_WIDTH, MODULE_HEIGHT), materials.anodizedBlack);
  body.position.y = MODULE_BOTTOM;
  body.castShadow = body.receiveShadow = true;
  group.add(body);

  for (const y of [MODULE_BOTTOM + 0.004, MODULE_BOTTOM + MODULE_HEIGHT - 0.012]) {
    const rail = new THREE.Mesh(ringGeometry(RADIUS + TRACK_HALF_WIDTH - 0.002, RADIUS + TRACK_HALF_WIDTH + 0.008, 0.008), materials.steel);
    rail.position.y = y;
    group.add(rail);
  }

  // Module seams + status LEDs every 250 mm (24 modules)
  const seamGeo = new THREE.BoxGeometry(0.003, MODULE_HEIGHT - 0.03, 0.004);
  const ledGeo = new THREE.BoxGeometry(0.018, 0.005, 0.003);
  const ledMat = materials.ledCyan;
  const moduleCount = Math.round(TRACK_LENGTH / 0.25);
  for (let i = 0; i < moduleCount; i++) {
    const deg = (i / moduleCount) * 360;
    const p = pathAt(deg);
    const seam = new THREE.Mesh(seamGeo, materials.aluminiumDark);
    seam.position.set(p.x + p.nx * (TRACK_HALF_WIDTH + 0.002), MODULE_BOTTOM + MODULE_HEIGHT / 2, p.z + p.nz * (TRACK_HALF_WIDTH + 0.002));
    seam.rotation.y = yawOf(p);
    group.add(seam);

    const pl = pathAt(deg + 180 / moduleCount);
    const led = new THREE.Mesh(ledGeo, ledMat);
    led.position.set(pl.x + pl.nx * (TRACK_HALF_WIDTH + 0.001), MODULE_BOTTOM + 0.03, pl.z + pl.nz * (TRACK_HALF_WIDTH + 0.001));
    led.rotation.y = yawOf(pl);
    group.add(led);
  }

  // Station markers on the track (dock positions)
  buildEntryStation(group);
  buildExitStation(group);

  scene.add(group);
  return group;
}

// ------------------------------------------------------------------ entry: cell-stack infeed gantry

let entryGripper = null;
let entryStack = null;

function buildEntryStation(group) {
  const p = pathAt(ENTRY_ANGLE);
  const station = new THREE.Group();
  station.position.set(p.x, 0, p.z);
  station.rotation.y = yawOf(p);
  // Local frame: +X along the track, +Z = outward normal (since yaw maps tangent to +X, normal to -Z ... flip)
  const outward = new THREE.Vector3(p.nx, 0, p.nz);
  const localOut = outward.clone().applyAxisAngle(new THREE.Vector3(0, 1, 0), -station.rotation.y);

  const postGeo = new THREE.BoxGeometry(0.05, 1.5, 0.05);
  for (const s of [-0.28, 0.28]) {
    const post = new THREE.Mesh(postGeo, materials.aluminium);
    post.position.set(s, 0.75, 0).addScaledVector(localOut, 0.35);
    post.castShadow = true;
    station.add(post);
  }
  const beam = new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.06, 0.06), materials.aluminium);
  beam.position.set(0, 1.5, 0).addScaledVector(localOut, 0.35);
  station.add(beam);

  const yBeam = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.06, 0.9), materials.beckhoffOrange);
  yBeam.position.set(0, 1.47, 0).addScaledVector(localOut, 0.3);
  station.add(yBeam);

  entryGripper = new THREE.Group();
  const zAxis = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.4, 0.05), materials.anodizedBlack);
  zAxis.position.y = -0.2;
  entryGripper.add(zAxis);
  const head = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.03, 0.13), materials.aluminiumDark);
  head.position.y = -0.41;
  entryGripper.add(head);
  entryGripper.userData.base = new THREE.Vector3(0, 1.44, 0).addScaledVector(localOut, TRACK_HALF_WIDTH + 0.07);
  entryGripper.position.copy(entryGripper.userData.base);
  station.add(entryGripper);

  // Infeed conveyor bringing 12S cell stacks
  const belt = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.06, 1.4), materials.rubber);
  belt.position.set(0, 0.86, 0).addScaledVector(localOut, 1.05);
  belt.castShadow = true;
  station.add(belt);
  const frame = new THREE.Mesh(new THREE.BoxGeometry(0.36, 0.05, 1.42), materials.aluminium);
  frame.position.copy(belt.position).add(new THREE.Vector3(0, -0.05, 0));
  station.add(frame);
  for (const s of [-0.15, 0.15]) {
    for (const k of [-0.6, 0.6]) {
      const leg = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.8, 0.04), materials.aluminiumDark);
      leg.position.copy(belt.position).add(new THREE.Vector3(s, -0.45, 0)).addScaledVector(localOut, k);
      station.add(leg);
    }
  }

  entryStack = new THREE.Group();
  entryStack.position.copy(belt.position).add(new THREE.Vector3(0, 0.03, 0)).addScaledVector(localOut, -0.45);
  station.add(entryStack);
  station.userData.localOut = localOut;
  group.add(station);
  group.userData.entryStation = station;
}

export function getEntryAnchors() {
  return { gripper: entryGripper, stack: entryStack };
}

// ------------------------------------------------------------------ exit: good / reject outfeed

let exitGood = null;
let exitReject = null;

function buildExitStation(group) {
  const p = pathAt(EXIT_ANGLE + 4);
  const station = new THREE.Group();
  station.position.set(p.x, 0, p.z);
  station.rotation.y = yawOf(p);
  const localOut = new THREE.Vector3(p.nx, 0, p.nz).applyAxisAngle(new THREE.Vector3(0, 1, 0), -station.rotation.y);

  const chute = (offsetAlong, color) => {
    const g = new THREE.Group();
    const bed = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.05, 1.1), new THREE.MeshStandardMaterial({ color: 0x24282e, roughness: 0.8 }));
    bed.position.set(offsetAlong, 0.8, 0).addScaledVector(localOut, 0.75);
    bed.castShadow = true;
    g.add(bed);
    const rail = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.03, 1.12), new THREE.MeshStandardMaterial({ color, roughness: 0.5, emissive: color, emissiveIntensity: 0.15 }));
    rail.position.copy(bed.position).add(new THREE.Vector3(0, -0.04, 0));
    g.add(rail);
    for (const k of [-0.45, 0.45]) {
      const leg = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.78, 0.04), materials.aluminiumDark);
      leg.position.copy(bed.position).add(new THREE.Vector3(0, -0.41, 0)).addScaledVector(localOut, k);
      g.add(leg);
    }
    station.add(g);
    const anchor = new THREE.Group();
    anchor.position.copy(bed.position).add(new THREE.Vector3(0, 0.03, 0));
    station.add(anchor);
    return anchor;
  };

  exitGood = chute(-0.18, 0x16a34a);
  exitReject = chute(0.18, 0xdc2626);
  station.userData.localOut = localOut;
  group.add(station);
  group.userData.exitStation = station;
}

export function getExitAnchors() {
  return { good: exitGood, reject: exitReject };
}

// ------------------------------------------------------------------ movers

const STATE_COLORS = {
  Moving: 0x22c55e,
  Loaded: 0x22c55e,
  Queued: 0xf59e0b,
  AtLoadStation: 0x3b82f6,
  AtUnloadStation: 0x3b82f6,
  AtEntryStation: 0x8b5cf6,
  AtExitStation: 0x8b5cf6,
  Idle: 0x64748b
};

export class MoverFleet {
  constructor(scene, count = 10) {
    this.group = new THREE.Group();
    this.group.name = 'movers';
    this.movers = [];
    const bodyGeo = new THREE.BoxGeometry(0.115, 0.1, 0.022);
    const magnetGeo = new THREE.BoxGeometry(0.1, 0.07, 0.004);
    const plateGeo = new THREE.BoxGeometry(0.17, 0.012, 0.15);
    const rollerGeo = new THREE.CylinderGeometry(0.011, 0.011, 0.012, 16);
    rollerGeo.rotateX(Math.PI / 2);
    const pinGeo = new THREE.CylinderGeometry(0.005, 0.005, 0.014, 10);
    const magnetMat = new THREE.MeshStandardMaterial({ color: 0x3a3f46, metalness: 0.8, roughness: 0.35 });

    for (let i = 0; i < count; i++) {
      const mover = new THREE.Group();
      const local = new THREE.Group(); // local: +X along track, -Z outward (yaw convention), so outward = -Z
      mover.add(local);

      const body = new THREE.Mesh(bodyGeo, materials.aluminium);
      body.position.set(0, MODULE_BOTTOM + MODULE_HEIGHT / 2 + 0.005, -(TRACK_HALF_WIDTH + 0.016));
      body.castShadow = true;
      local.add(body);
      const magnet = new THREE.Mesh(magnetGeo, magnetMat);
      magnet.position.set(0, body.position.y, -(TRACK_HALF_WIDTH + 0.004));
      local.add(magnet);

      for (const sx of [-0.04, 0.04]) {
        for (const y of [MODULE_BOTTOM + 0.012, MODULE_BOTTOM + MODULE_HEIGHT - 0.006]) {
          const roller = new THREE.Mesh(rollerGeo, materials.rubber);
          roller.position.set(sx, y, -(TRACK_HALF_WIDTH + 0.012));
          local.add(roller);
        }
      }

      const plate = new THREE.Mesh(plateGeo, materials.aluminiumDark);
      plate.position.set(0, TRACK_TOP - 0.006, -(TRACK_HALF_WIDTH + 0.055));
      plate.castShadow = plate.receiveShadow = true;
      local.add(plate);
      for (const sx of [-0.07, 0.07]) {
        const pin = new THREE.Mesh(pinGeo, materials.steel);
        pin.position.set(sx, TRACK_TOP + 0.006, plate.position.z + 0.05);
        local.add(pin);
      }

      const ledMat = new THREE.MeshStandardMaterial({ color: 0x111111, emissive: 0x22c55e, emissiveIntensity: 2.5 });
      const led = new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.008, 0.003), ledMat);
      led.position.set(0.035, body.position.y + 0.035, -(TRACK_HALF_WIDTH + 0.028));
      local.add(led);

      const idTag = makeIdTag(i);
      idTag.position.set(-0.025, body.position.y + 0.012, -(TRACK_HALF_WIDTH + 0.0275));
      idTag.rotation.y = Math.PI;
      local.add(idTag);

      const carrier = new THREE.Group();
      carrier.position.set(0, TRACK_TOP, plate.position.z);
      local.add(carrier);

      const tagEl = document.createElement('div');
      tagEl.className = 'mover-tag';
      const tag = new CSS2DObject(tagEl);
      tag.position.set(0, TRACK_TOP + 0.17, plate.position.z);
      local.add(tag);

      this.group.add(mover);
      this.movers.push({ id: i, object: mover, carrier, ledMat, tag, tagEl, pos: i * 36, lastTag: '' });
    }

    scene.add(this.group);
  }

  update(frame, showTags) {
    if (!frame) return;
    for (const m of frame.movers) {
      const mover = this.movers[m.id];
      if (!mover) continue;
      const p = pathAt(m.pos);
      mover.pos = m.pos;
      mover.object.position.set(p.x, 0, p.z);
      mover.object.rotation.y = yawOf(p);
      // ensure outward normal maps to local -Z: yaw rotates +X→tangent; with y-up right-handed frame -Z→left of travel
      const color = STATE_COLORS[m.state] ?? 0x22c55e;
      mover.ledMat.emissive.setHex(color);

      const text = m.part ? m.part.trk.replace('TRK-', '#') : '';
      if (text !== mover.lastTag) {
        mover.tagEl.textContent = text;
        mover.tagEl.className = 'mover-tag' + (m.part?.status === 'Bad' ? ' bad' : m.part?.status === 'Good' ? ' good' : '');
        mover.lastTag = text;
      }
      mover.tag.visible = showTags && text !== '';
    }
  }

  carrierOf(id) {
    return this.movers[id]?.carrier;
  }

  worldPositionOf(id, target = new THREE.Vector3()) {
    return this.movers[id]?.carrier.getWorldPosition(target);
  }
}

function makeIdTag(n) {
  const canvas = document.createElement('canvas');
  canvas.width = 64; canvas.height = 32;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#f2f2f2';
  ctx.fillRect(0, 0, 64, 32);
  ctx.fillStyle = '#111';
  ctx.font = 'bold 24px sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(String(n).padStart(2, '0'), 32, 17);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  return new THREE.Mesh(new THREE.PlaneGeometry(0.028, 0.014), new THREE.MeshStandardMaterial({ map: tex, roughness: 0.6 }));
}

export { offsetPoint };
