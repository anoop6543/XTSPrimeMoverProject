// Robot cells: base cabinet, rotary index dial with process tools, guarding with light curtain,
// andon tower, live HMI screen, outfeed nest, cell robot and a maintenance technician.
import * as THREE from 'three';
import { CSS2DObject } from 'three/addons/renderers/CSS2DRenderer.js';
import { materials } from './factory.js';
import { CellRobot } from './robots.js';
import { MACHINE_ANGLES, CELL_OFFSET, TRACK_TOP, TRACK_HALF_WIDTH, pathAt, yawOf, smooth } from './layout.js';

const DIAL_TOP = 0.9;
const DIAL_RADIUS = 0.3;
const SLOT_RADIUS = 0.2;

const TOOLSETS = {
  LaserWelding: ['press', 'laser', 'nozzle', 'scanner'],
  PrecisionAssembly: ['gripper', 'gripper', 'nutrunner', 'probe', 'camera'],
  QualityInspection: ['vision3d', 'probe', 'scanner', 'scale'],
  FunctionalTesting: ['hipot', 'probe', 'probe', 'probe', 'marker']
};

const ACCENTS = {
  LaserWelding: 0xff6b35,
  PrecisionAssembly: 0x00c875,
  QualityInspection: 0x3fb6ff,
  FunctionalTesting: 0xffc400
};

function emissiveMat(color, intensity = 0) {
  return new THREE.MeshStandardMaterial({ color: 0x111111, emissive: color, emissiveIntensity: intensity, roughness: 0.4 });
}

function slotAngle(index, count) {
  // Station 0 faces the track (+Z local); stations run clockwise seen from above.
  return -index * (2 * Math.PI / count);
}

function slotPosition(index, count, radius = SLOT_RADIUS) {
  const a = slotAngle(index, count);
  return new THREE.Vector3(Math.sin(a) * radius, 0, Math.cos(a) * radius);
}

class ProcessTool {
  constructor(kind, accent) {
    this.kind = kind;
    this.group = new THREE.Group();
    this.head = new THREE.Group();
    this.activity = 0;
    this.time = 0;

    const column = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.42, 0.05), materials.aluminium);
    column.position.set(0, DIAL_TOP + 0.21, 0.42);
    column.castShadow = true;
    this.group.add(column);
    const arm = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.04, 0.24), materials.aluminiumDark);
    arm.position.set(0, DIAL_TOP + 0.4, (0.42 + SLOT_RADIUS) / 2);
    this.group.add(arm);
    this.head.position.set(0, DIAL_TOP + 0.3, SLOT_RADIUS);
    this.group.add(this.head);

    const body = new THREE.Mesh(new THREE.BoxGeometry(0.07, 0.12, 0.07), kind === 'laser' || kind === 'marker' ? materials.anodizedBlack : materials.cabinetDark);
    body.castShadow = true;
    this.head.add(body);
    const stripe = new THREE.Mesh(new THREE.BoxGeometry(0.072, 0.015, 0.072), new THREE.MeshStandardMaterial({ color: accent, roughness: 0.4 }));
    stripe.position.y = 0.04;
    this.head.add(stripe);

    this.glowMat = emissiveMat(accent, 0);
    switch (kind) {
      case 'laser':
      case 'marker': {
        const nozzle = new THREE.Mesh(new THREE.CylinderGeometry(0.012, 0.02, 0.06, 16), materials.steel);
        nozzle.position.y = -0.09;
        this.head.add(nozzle);
        const beamColor = kind === 'laser' ? 0xff3d00 : 0x22ff88;
        this.beamMat = new THREE.MeshBasicMaterial({ color: new THREE.Color(beamColor).multiplyScalar(4), transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false });
        this.beam = new THREE.Mesh(new THREE.CylinderGeometry(0.0025, 0.0025, 0.085, 8), this.beamMat);
        this.beam.position.y = -0.1625;
        this.head.add(this.beam);
        this.spotMat = new THREE.MeshBasicMaterial({ color: new THREE.Color(kind === 'laser' ? 0xffd28a : 0xb8ffd8).multiplyScalar(6), transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false });
        this.spot = new THREE.Mesh(new THREE.SphereGeometry(0.012, 12, 8), this.spotMat);
        this.spot.position.y = -0.205;
        this.head.add(this.spot);
        if (kind === 'laser') {
          this.light = new THREE.PointLight(0xff7a2a, 0, 0.9, 2);
          this.light.position.y = -0.19;
          this.head.add(this.light);
          const duct = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.025, 0.3, 14), materials.steel);
          duct.rotation.z = Math.PI / 4;
          duct.position.set(0.09, 0.05, 0);
          this.head.add(duct);
        }
        break;
      }
      case 'camera':
      case 'vision3d': {
        const lens = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.018, 0.03, 16), materials.anodizedBlack);
        lens.position.y = -0.075;
        this.head.add(lens);
        const ring = new THREE.Mesh(new THREE.TorusGeometry(0.04, 0.007, 10, 32), this.glowMat);
        ring.rotation.x = Math.PI / 2;
        ring.position.y = -0.085;
        this.head.add(ring);
        if (kind === 'vision3d') {
          this.patternMat = new THREE.MeshBasicMaterial({ map: stripePattern(), transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false, color: 0x6cc6ff });
          this.pattern = new THREE.Mesh(new THREE.PlaneGeometry(0.22, 0.13), this.patternMat);
          this.pattern.rotation.x = -Math.PI / 2;
          this.pattern.position.y = -0.18;
          this.head.add(this.pattern);
        }
        break;
      }
      case 'scanner': {
        this.lineMat = new THREE.MeshBasicMaterial({ color: new THREE.Color(0x4dff88).multiplyScalar(3), transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide });
        this.line = new THREE.Mesh(new THREE.PlaneGeometry(0.004, 0.13), this.lineMat);
        this.line.rotation.x = -Math.PI / 2;
        this.line.position.y = -0.18;
        this.head.add(this.line);
        this.fanMat = new THREE.MeshBasicMaterial({ color: 0x4dff88, transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide });
        const fan = new THREE.Mesh(new THREE.ConeGeometry(0.065, 0.14, 3, 1, true), this.fanMat);
        fan.position.y = -0.13;
        fan.rotation.y = Math.PI / 2;
        this.fan = fan;
        this.head.add(fan);
        break;
      }
      case 'nutrunner': {
        this.spindle = new THREE.Mesh(new THREE.CylinderGeometry(0.014, 0.014, 0.12, 12), materials.steel);
        this.spindle.position.y = -0.11;
        this.head.add(this.spindle);
        const bit = new THREE.Mesh(new THREE.CylinderGeometry(0.004, 0.006, 0.03, 8), materials.anodizedBlack);
        bit.position.y = -0.065;
        this.spindle.add(bit);
        break;
      }
      case 'press': {
        const rod = new THREE.Mesh(new THREE.CylinderGeometry(0.012, 0.012, 0.1, 12), materials.steel);
        rod.position.y = -0.1;
        this.head.add(rod);
        const platen = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.012, 0.12), materials.aluminiumDark);
        platen.position.y = -0.155;
        this.head.add(platen);
        break;
      }
      case 'hipot': {
        this.beaconMat = emissiveMat(0xff2020, 0);
        const beacon = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.022, 0.04, 16), this.beaconMat);
        beacon.position.set(0, 0.09, 0);
        this.head.add(beacon);
        this.beacon = beacon;
      }
      // falls through to pins
      case 'probe': {
        const block = new THREE.Mesh(new THREE.BoxGeometry(0.16, 0.02, 0.08), materials.cabinetDark);
        block.position.y = -0.08;
        this.head.add(block);
        for (let i = 0; i < 6; i++) {
          const pin = new THREE.Mesh(new THREE.CylinderGeometry(0.002, 0.002, 0.03, 6), materials.steel);
          pin.position.set(-0.06 + i * 0.024, -0.1, 0.02 * (i % 2 ? 1 : -1));
          this.head.add(pin);
        }
        const led = new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.01, 0.005), this.glowMat);
        led.position.set(0, -0.06, 0.04);
        this.head.add(led);
        break;
      }
      case 'nozzle': {
        for (const dx of [-0.05, 0.05]) {
          const n = new THREE.Mesh(new THREE.CylinderGeometry(0.006, 0.01, 0.06, 10), new THREE.MeshStandardMaterial({ color: 0x2b6cff, roughness: 0.4 }));
          n.position.set(dx, -0.09, 0);
          n.rotation.z = dx > 0 ? -0.4 : 0.4;
          this.head.add(n);
        }
        this.mistMat = new THREE.MeshBasicMaterial({ color: 0x9fd8ff, transparent: true, opacity: 0, depthWrite: false, blending: THREE.AdditiveBlending });
        const mist = new THREE.Mesh(new THREE.ConeGeometry(0.08, 0.14, 20, 1, true), this.mistMat);
        mist.position.y = -0.18;
        mist.rotation.x = Math.PI;
        this.head.add(mist);
        break;
      }
      case 'scale': {
        const plate = new THREE.Mesh(new THREE.BoxGeometry(0.2, 0.01, 0.13), materials.steel);
        plate.position.y = -0.27;
        this.head.add(plate);
        break;
      }
      case 'gripper':
      default: {
        for (const dz of [-0.035, 0.035]) {
          const finger = new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.06, 0.01), materials.aluminiumDark);
          finger.position.set(0, -0.09, dz);
          this.head.add(finger);
        }
      }
    }
  }

  update(active, progress, dt, flicker) {
    this.time += dt;
    const target = active ? 1 : 0;
    this.activity += (target - this.activity) * (1 - Math.exp(-dt * 10));
    const a = this.activity;
    const stroke = this.kind === 'scale' || this.kind === 'scanner' || this.kind === 'camera' || this.kind === 'vision3d' || this.kind === 'laser' || this.kind === 'marker' ? 0.0
      : this.kind === 'probe' || this.kind === 'hipot' ? 0.07 : 0.05;
    this.head.position.y = DIAL_TOP + 0.3 - stroke * smooth(a);
    this.glowMat.emissiveIntensity = 0.3 + 3.5 * a;

    if (this.beam) {
      const pulse = this.kind === 'laser' ? 0.75 + 0.25 * Math.sin(this.time * 90 + flicker) : (Math.sin(this.time * 40) > 0 ? 1 : 0.2);
      const on = a > 0.6 ? 1 : 0;
      this.beamMat.opacity = on * 0.9 * pulse;
      this.spotMat.opacity = on * pulse;
      this.spot.scale.setScalar(0.8 + 0.6 * pulse);
      // trace the weld seam across the busbars
      const sweep = Math.sin(progress * Math.PI * 6) * 0.07;
      this.beam.position.x = this.spot.position.x = sweep;
      if (this.light) this.light.intensity = on * 4 * pulse;
    }
    if (this.patternMat) {
      this.patternMat.opacity = a * (0.55 + 0.25 * Math.sin(this.time * 12));
      this.patternMat.map.offset.x = (this.time * 0.6) % 1;
    }
    if (this.line) {
      this.lineMat.opacity = a * 0.95;
      this.fanMat.opacity = a * 0.12;
      this.line.position.x = (progress - 0.5) * 0.18;
      this.fan.position.x = this.line.position.x;
    }
    if (this.spindle) {
      this.spindle.rotation.y += dt * 40 * a;
      this.spindle.position.y = -0.11 - 0.015 * a * Math.min(1, progress * 1.4);
    }
    if (this.beacon) {
      this.beaconMat.emissiveIntensity = a * (Math.sin(this.time * 14) > 0 ? 4 : 0.4);
    }
    if (this.mistMat) this.mistMat.opacity = a * 0.18;
  }
}

function stripePattern() {
  const canvas = document.createElement('canvas');
  canvas.width = 256; canvas.height = 64;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#000';
  ctx.fillRect(0, 0, 256, 64);
  for (let x = 0; x < 256; x += 16) {
    const g = ctx.createLinearGradient(x, 0, x + 16, 0);
    g.addColorStop(0, 'rgba(255,255,255,0)');
    g.addColorStop(0.5, 'rgba(255,255,255,1)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = g;
    ctx.fillRect(x, 0, 16, 64);
  }
  const tex = new THREE.CanvasTexture(canvas);
  tex.wrapS = THREE.RepeatWrapping;
  return tex;
}

function makeTechnician() {
  const g = new THREE.Group();
  const skin = new THREE.MeshStandardMaterial({ color: 0xc58c64, roughness: 0.7 });
  const vest = new THREE.MeshStandardMaterial({ color: 0xff8c00, emissive: 0x552200, emissiveIntensity: 0.4, roughness: 0.6 });
  const pants = new THREE.MeshStandardMaterial({ color: 0x1f2a44, roughness: 0.8 });
  const helmet = new THREE.MeshStandardMaterial({ color: 0xf5f5f5, roughness: 0.4 });
  for (const dx of [-0.07, 0.07]) {
    const leg = new THREE.Mesh(new THREE.CapsuleGeometry(0.055, 0.7, 6, 12), pants);
    leg.position.set(dx, 0.42, 0);
    g.add(leg);
  }
  const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.15, 0.42, 6, 14), vest);
  torso.position.y = 1.12;
  torso.scale.z = 0.7;
  g.add(torso);
  for (const dx of [-0.2, 0.2]) {
    const arm = new THREE.Mesh(new THREE.CapsuleGeometry(0.045, 0.5, 6, 10), vest);
    arm.position.set(dx, 1.12, 0.12);
    arm.rotation.x = -0.9;
    g.add(arm);
  }
  const head = new THREE.Mesh(new THREE.SphereGeometry(0.1, 20, 14), skin);
  head.position.y = 1.55;
  g.add(head);
  const hat = new THREE.Mesh(new THREE.SphereGeometry(0.115, 20, 10, 0, Math.PI * 2, 0, Math.PI / 2), helmet);
  hat.position.y = 1.58;
  g.add(hat);
  const brim = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.14, 0.01, 20), helmet);
  brim.position.y = 1.58;
  g.add(brim);
  g.traverse((o) => { if (o.isMesh) o.castShadow = true; });
  return g;
}

export class MachineCell {
  constructor(scene, index, type, name) {
    this.index = index;
    this.type = type;
    this.name = name;
    this.angle = MACHINE_ANGLES[index];
    const p = pathAt(this.angle);
    this.group = new THREE.Group();
    this.group.position.set(p.x + p.nx * CELL_OFFSET, 0, p.z + p.nz * CELL_OFFSET);
    this.group.rotation.y = yawOf(p); // yaw convention: local -Z = outward, so local +Z faces the track
    scene.add(this.group);

    const accent = ACCENTS[type] ?? 0x3fb6ff;

    // Base cabinet with plinth and service doors
    const cabinet = new THREE.Mesh(new THREE.BoxGeometry(1.0, 0.82, 0.9), materials.cabinet);
    cabinet.position.y = 0.45;
    cabinet.castShadow = cabinet.receiveShadow = true;
    this.group.add(cabinet);
    const plinth = new THREE.Mesh(new THREE.BoxGeometry(1.02, 0.06, 0.92), materials.cabinetDark);
    plinth.position.y = 0.03;
    this.group.add(plinth);
    const topPlate = new THREE.Mesh(new THREE.BoxGeometry(1.04, 0.04, 0.94), materials.aluminiumMatte);
    topPlate.position.y = 0.88;
    topPlate.receiveShadow = true;
    this.group.add(topPlate);
    for (const dx of [-0.25, 0.25]) {
      const door = new THREE.Mesh(new THREE.PlaneGeometry(0.46, 0.66), new THREE.MeshStandardMaterial({ color: 0xa6aba7, roughness: 0.65 }));
      door.position.set(dx, 0.46, 0.451);
      this.group.add(door);
      const handle = new THREE.Mesh(new THREE.BoxGeometry(0.015, 0.12, 0.02), materials.anodizedBlack);
      handle.position.set(dx + (dx > 0 ? -0.19 : 0.19), 0.5, 0.465);
      this.group.add(handle);
    }
    const accentBand = new THREE.Mesh(new THREE.BoxGeometry(1.005, 0.03, 0.905), new THREE.MeshStandardMaterial({ color: accent, roughness: 0.4 }));
    accentBand.position.y = 0.84;
    this.group.add(accentBand);

    // Rotary index dial
    this.dial = new THREE.Group();
    this.dial.position.y = DIAL_TOP - 0.02;
    this.group.add(this.dial);
    const dialDisc = new THREE.Mesh(new THREE.CylinderGeometry(DIAL_RADIUS, DIAL_RADIUS, 0.04, 48), materials.aluminiumMatte);
    dialDisc.castShadow = dialDisc.receiveShadow = true;
    this.dial.add(dialDisc);
    const hub = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 0.05, 24), materials.anodizedBlack);
    hub.position.y = 0.02;
    this.dial.add(hub);
    this.stationCount = (TOOLSETS[type] ?? []).length || 4;
    for (let i = 0; i < this.stationCount; i++) {
      const nest = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.012, 0.06), materials.cabinetDark);
      const sp = slotPosition(i, this.stationCount);
      nest.position.set(sp.x, 0.026, sp.z);
      this.dial.add(nest);
    }
    this.partAnchor = new THREE.Group();
    this.partAnchor.position.set(0, 0.02, SLOT_RADIUS); // load slot; the dial carries it from station to station
    this.dial.add(this.partAnchor);
    this.dialAngle = 0;
    this.partAngle = 0;

    // Process tools around the dial
    this.tools = (TOOLSETS[type] ?? []).map((kind, i) => {
      const tool = new ProcessTool(kind, accent);
      const a = slotAngle(i, this.stationCount);
      tool.group.rotation.y = a;
      this.group.add(tool.group);
      return tool;
    });
    // Station 0 tool would collide with the robot loading path → lift it away from the load slot.
    if (this.tools[0]) this.tools[0].group.position.y = 0.04;

    // Guarding: aluminium profile frame + polycarbonate, light curtain on the robot side
    const frameGeo = new THREE.BoxGeometry(0.04, 1.2, 0.04);
    for (const [x, z] of [[-0.5, -0.45], [0.5, -0.45], [-0.5, 0.45], [0.5, 0.45]]) {
      const post = new THREE.Mesh(frameGeo, materials.aluminium);
      post.position.set(x, 1.5, z);
      post.castShadow = true;
      this.group.add(post);
    }
    for (const [w, d, x, z] of [[1.04, 0.04, 0, -0.45], [1.04, 0.04, 0, 0.45], [0.04, 0.94, -0.5, 0], [0.04, 0.94, 0.5, 0]]) {
      const rail = new THREE.Mesh(new THREE.BoxGeometry(w, 0.04, d), materials.aluminium);
      rail.position.set(x, 2.1, z);
      this.group.add(rail);
    }
    const panelBack = new THREE.Mesh(new THREE.PlaneGeometry(0.96, 1.16), materials.glass);
    panelBack.position.set(0, 1.5, -0.45);
    this.group.add(panelBack);
    for (const sx of [-0.5, 0.5]) {
      const side = new THREE.Mesh(new THREE.PlaneGeometry(0.86, 1.16), materials.glass);
      side.rotation.y = Math.PI / 2;
      side.position.set(sx, 1.5, 0);
      this.group.add(side);
    }
    this.curtainMat = emissiveMat(0xff1f1f, 1.2);
    for (const sx of [-0.47, 0.47]) {
      const lc = new THREE.Mesh(new THREE.BoxGeometry(0.025, 0.9, 0.025), materials.anodizedBlack);
      lc.position.set(sx, 1.35, 0.47);
      this.group.add(lc);
      const slit = new THREE.Mesh(new THREE.BoxGeometry(0.006, 0.86, 0.004), this.curtainMat);
      slit.position.set(sx + (sx > 0 ? -0.013 : 0.013), 1.35, 0.483);
      this.group.add(slit);
    }

    // Andon stack light: blue / red / amber / green
    this.andon = {};
    const andonBase = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.025, 0.05, 16), materials.anodizedBlack);
    andonBase.position.set(0.44, 2.15, -0.4);
    this.group.add(andonBase);
    [['green', 0x16ff6a], ['amber', 0xffb020], ['red', 0xff2a2a], ['blue', 0x2f7bff]].forEach(([key, color], i) => {
      const mat = new THREE.MeshStandardMaterial({ color: new THREE.Color(color).multiplyScalar(0.25), emissive: color, emissiveIntensity: 0, roughness: 0.3, transparent: true, opacity: 0.92 });
      const seg = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.055, 20), mat);
      seg.position.set(0.44, 2.21 + i * 0.058, -0.4);
      this.group.add(seg);
      this.andon[key] = mat;
    });

    // HMI panel on a swing arm
    const armPole = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.015, 0.5, 10), materials.aluminium);
    armPole.position.set(-0.58, 1.15, 0.36);
    this.group.add(armPole);
    this.hmiCanvas = document.createElement('canvas');
    this.hmiCanvas.width = 320; this.hmiCanvas.height = 220;
    this.hmiTexture = new THREE.CanvasTexture(this.hmiCanvas);
    this.hmiTexture.colorSpace = THREE.SRGBColorSpace;
    const hmiFrame = new THREE.Mesh(new THREE.BoxGeometry(0.34, 0.25, 0.03), materials.anodizedBlack);
    hmiFrame.position.set(-0.58, 1.45, 0.38);
    hmiFrame.rotation.y = 0.5;
    this.group.add(hmiFrame);
    const hmiScreen = new THREE.Mesh(new THREE.PlaneGeometry(0.31, 0.215), new THREE.MeshStandardMaterial({ map: this.hmiTexture, emissive: 0xffffff, emissiveMap: this.hmiTexture, emissiveIntensity: 0.9, roughness: 0.3 }));
    hmiScreen.position.set(0, 0, 0.016);
    hmiFrame.add(hmiScreen);
    this.hmiTimer = 1;

    // Outfeed nest stand (track side, left of the robot)
    const stand = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.86, 0.18), materials.cabinetDark);
    stand.position.set(-0.32, 0.43, 0.66);
    stand.castShadow = true;
    this.group.add(stand);
    const nestPlate = new THREE.Mesh(new THREE.BoxGeometry(0.24, 0.02, 0.16), materials.aluminium);
    nestPlate.position.set(-0.32, 0.87, 0.66);
    this.group.add(nestPlate);
    this.nestAnchor = new THREE.Group();
    this.nestAnchor.position.set(-0.32, 0.88, 0.66);
    this.group.add(this.nestAnchor);

    // Cell robot between the dock and the dial
    this.robot = new CellRobot(this.group, new THREE.Vector3(0.34, 0, 0.66), 0);

    // Technician (visible during maintenance)
    this.technician = makeTechnician();
    this.technician.position.set(-0.75, 0, 0.95);
    this.technician.rotation.y = -2.4;
    this.technician.visible = false;
    this.group.add(this.technician);

    // Floating cell label
    this.labelEl = document.createElement('div');
    this.labelEl.className = 'cell-label';
    this.labelEl.innerHTML = `<div class="name"><span>${name}</span><span class="tags"></span></div>
      <div class="row"><span>State</span><b class="state">–</b></div>
      <div class="row"><span>Health</span><b class="health">–</b></div>
      <div class="row"><span>RUL</span><b class="rul">–</b></div>
      <div class="bar"><i class="hbar" style="width:100%"></i></div>`;
    this.label = new CSS2DObject(this.labelEl);
    this.label.position.set(0, 2.55, 0);
    this.group.add(this.label);
    this.labelRefs = {
      tags: this.labelEl.querySelector('.tags'),
      state: this.labelEl.querySelector('.state'),
      health: this.labelEl.querySelector('.health'),
      rul: this.labelEl.querySelector('.rul'),
      bar: this.labelEl.querySelector('.hbar')
    };
    this.labelCache = {};
    this.time = 0;
  }

  /** World-space targets for the robot: carrier on the docked mover, dial load slot, outfeed nest. */
  robotPoints() {
    const p = pathAt(this.angle);
    const carrierOut = TRACK_HALF_WIDTH + 0.055;
    const mover = new THREE.Vector3(p.x + p.nx * carrierOut, TRACK_TOP, p.z + p.nz * carrierOut);
    const loadSlot = slotPosition(0, this.stationCount);
    const machine = this.group.localToWorld(new THREE.Vector3(loadSlot.x, DIAL_TOP + 0.02, loadSlot.z));
    const nest = this.group.localToWorld(new THREE.Vector3(-0.32, 0.88, 0.66));
    return { mover, machine, nest };
  }

  update(m, robotState, dt) {
    if (!m) return;
    this.time += dt;
    const count = m.stations.length || this.stationCount;
    const activeIdx = m.stations.findIndex((s) => s.part);
    const targetAngle = activeIdx >= 0 ? slotAngle(activeIdx, count) : 0;
    // Index the dial smoothly to the station holding the module (real indexers take ~0.3 s)
    let diff = targetAngle - this.partAngle;
    while (diff > Math.PI) diff -= 2 * Math.PI;
    while (diff < -Math.PI) diff += 2 * Math.PI;
    this.partAngle += diff * (1 - Math.exp(-dt * 9));
    this.dial.rotation.y = this.partAngle;

    const flicker = this.index * 1.7;
    this.tools.forEach((tool, i) => {
      const st = m.stations[i];
      tool.update(st?.st === 'Processing', st?.p ?? 0, dt, flicker);
    });

    // Andon logic
    const blink = Math.sin(this.time * 7) > 0;
    const maint = m.maint !== 'None';
    const breakdown = maint && m.maintKind === 'Breakdown';
    const running = m.activity === 'Running';
    const idle = m.activity === 'Starved' || m.activity === 'Blocked';
    this.andon.green.emissiveIntensity = running && !breakdown ? 3 : 0.05;
    this.andon.amber.emissiveIntensity = (idle && !maint) || (m.anomalyFlag && blink) ? 2.6 : 0.05;
    this.andon.red.emissiveIntensity = breakdown || m.fault ? (blink ? 4 : 0.3) : 0.05;
    this.andon.blue.emissiveIntensity = maint ? (m.maint === 'InProgress' ? 3 : (blink ? 3 : 0.2)) : 0.05;
    this.curtainMat.emissiveIntensity = maint ? 0.2 : 1.2;
    this.technician.visible = m.maint === 'InProgress';
    if (this.technician.visible) {
      this.technician.rotation.y = -2.4 + Math.sin(this.time * 1.3) * 0.25;
    }

    this.robot.update(robotState, this.robotPoints(), dt);

    this.hmiTimer += dt;
    if (this.hmiTimer > 0.5) {
      this.hmiTimer = 0;
      this.drawHmi(m);
    }

    this.updateLabel(m);
  }

  drawHmi(m) {
    const c = this.hmiCanvas.getContext('2d');
    c.fillStyle = '#0b1118';
    c.fillRect(0, 0, 320, 220);
    c.fillStyle = '#16212d';
    c.fillRect(0, 0, 320, 34);
    c.fillStyle = '#e8eef5';
    c.font = '600 18px "Segoe UI", sans-serif';
    c.fillText(this.name.toUpperCase(), 12, 23);
    const stateColor = m.maint !== 'None' ? '#3b82f6' : m.activity === 'Running' ? '#22c55e' : m.activity === 'Down' ? '#ef4444' : '#f59e0b';
    c.fillStyle = stateColor;
    c.beginPath(); c.arc(298, 17, 7, 0, Math.PI * 2); c.fill();
    c.font = '14px "Segoe UI", sans-serif';
    c.fillStyle = '#8ea0b4';
    const rows = [
      ['State', m.maint !== 'None' ? `MAINT ${m.maint}` : (m.activity || m.seq)],
      ['Health', `${Math.round((m.health ?? 1) * 100)} %`],
      ['RUL', m.rul == null ? 'learning' : fmtDuration(m.rul)],
      ['Temp', `${(m.temp ?? 0).toFixed(1)} °C`],
      ['Vibration', `${(m.vib ?? 0).toFixed(2)} mm/s`],
      ['OEE', `${Math.round((m.oee ?? 0) * 100)} %`]
    ];
    rows.forEach(([k, v], i) => {
      c.fillStyle = '#8ea0b4';
      c.fillText(k, 12, 60 + i * 25);
      c.fillStyle = '#e8eef5';
      c.fillText(String(v), 120, 60 + i * 25);
    });
    const h = m.health ?? 1;
    c.fillStyle = '#1f2a37';
    c.fillRect(230, 52, 76, 150);
    c.fillStyle = h > 0.7 ? '#22c55e' : h > 0.45 ? '#f59e0b' : '#ef4444';
    c.fillRect(230, 52 + 150 * (1 - h), 76, 150 * h);
    this.hmiTexture.needsUpdate = true;
  }

  updateLabel(m) {
    const r = this.labelRefs;
    const cache = this.labelCache;
    const tags = [];
    if (m.bottleneck) tags.push('<span class="tag constraint">CONSTRAINT</span>');
    if (m.anomalyFlag) tags.push('<span class="tag anomaly">ANOMALY</span>');
    if (m.spcAlarm) tags.push('<span class="tag spc">SPC</span>');
    if (m.maint !== 'None') tags.push(`<span class="tag maint">${m.maintKind === 'Breakdown' ? 'BREAKDOWN' : 'PM'}</span>`);
    const tagHtml = tags.join(' ');
    if (cache.tags !== tagHtml) { r.tags.innerHTML = tagHtml; cache.tags = tagHtml; }

    const state = m.maint !== 'None'
      ? `${m.maint === 'InProgress' ? 'Maintenance' : 'Draining'}${m.maint === 'InProgress' ? ` ${Math.ceil(m.maintLeft)} s` : ''}`
      : (m.activity || '–');
    if (cache.state !== state) { r.state.textContent = state; cache.state = state; }
    const health = `${Math.round((m.health ?? 1) * 100)} %`;
    if (cache.health !== health) {
      r.health.textContent = health;
      this.labelEl.dataset.health = health;
      const h = m.health ?? 1;
      r.bar.style.width = `${Math.max(2, h * 100)}%`;
      r.bar.style.background = h > 0.7 ? 'var(--good)' : h > 0.45 ? 'var(--warn)' : 'var(--bad)';
      cache.health = health;
    }
    const rul = m.rul == null ? 'learning' : fmtDuration(m.rul);
    if (cache.rul !== rul) { r.rul.textContent = rul; cache.rul = rul; }
    const cls = 'cell-label' + (m.anomalyFlag || (m.maintKind === 'Breakdown') ? ' alarm' : m.maint !== 'None' ? ' maint' : '');
    if (cache.cls !== cls) { this.labelEl.className = cls; cache.cls = cls; }
  }
}

export function fmtDuration(s) {
  if (s == null || !isFinite(s)) return '∞';
  s = Math.max(0, Math.round(s));
  if (s >= 3600) return `${Math.floor(s / 3600)}h ${String(Math.floor((s % 3600) / 60)).padStart(2, '0')}m`;
  if (s >= 60) return `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, '0')}s`;
  return `${s}s`;
}

export function buildCells(scene, frame) {
  const defaults = [
    { id: 0, type: 'LaserWelding', name: 'Laser Welder' },
    { id: 1, type: 'PrecisionAssembly', name: 'Assembler' },
    { id: 2, type: 'QualityInspection', name: 'Inspector' },
    { id: 3, type: 'FunctionalTesting', name: 'Tester' }
  ];
  const machines = frame?.machines?.length ? frame.machines : defaults;
  return machines.map((m, i) => new MachineCell(scene, i, m.type, m.name));
}
