// Camera presets, follow-cam and scripted cinematic tours (deterministic in time for video capture).
import * as THREE from 'three';
import { pathAt, offsetPoint, MACHINE_ANGLES, CELL_OFFSET, ENTRY_ANGLE, EXIT_ANGLE, smooth, lerp } from './layout.js';
import { fmtDuration } from './cells.js';

const UP = new THREE.Vector3(0, 1, 0);

export function overviewPose() {
  return { pos: new THREE.Vector3(4.4, 3.4, 5.2), target: new THREE.Vector3(0, 0.85, 0.1) };
}

/**
 * Cell view from inside the loop looking outward: docked mover in the foreground, robot and
 * rotary dial behind it (how you would photograph a real XTS cell). `orbit` swings left/right.
 */
export function cellPose(index, distance = 1.0, orbit = 0) {
  const deg = MACHINE_ANGLES[index];
  const p = pathAt(deg);
  const t = new THREE.Vector3(p.tx, 0, p.tz);
  const n = new THREE.Vector3(p.nx, 0, p.nz);
  const target = offsetPoint(deg, CELL_OFFSET * 0.62, 0, 1.0);
  const dock = offsetPoint(deg, 0, 0, 0);
  const dir = n.clone().multiplyScalar(-Math.cos(orbit)).addScaledVector(t, Math.sin(orbit)).normalize();
  const pos = dock.clone().addScaledVector(dir, 0.55 + 0.75 * distance).addScaledVector(UP, 1.0 + 0.55 * distance);
  return { pos, target };
}

export function moverFollowPose(fleet, id, lead = 0.35) {
  const mover = fleet.movers[id];
  if (!mover) return overviewPose();
  const p = pathAt(mover.pos);
  const t = new THREE.Vector3(p.tx, 0, p.tz);
  const n = new THREE.Vector3(p.nx, 0, p.nz);
  const carrier = fleet.worldPositionOf(id);
  // Chase from inside the loop (behind and inboard) so cells never block the view.
  const pos = carrier.clone().addScaledVector(t, -0.62).addScaledVector(n, -0.42).addScaledVector(UP, 0.36);
  const target = carrier.clone().addScaledVector(t, lead).addScaledVector(n, 0.08).addScaledVector(UP, 0.02);
  return { pos, target };
}

function blendPose(a, b, t) {
  const k = smooth(t);
  return { pos: a.pos.clone().lerp(b.pos, k), target: a.target.clone().lerp(b.target, k) };
}

function stationCaption(i, m) {
  const titles = [
    ['M0 · Cell stacking + busbar laser welding', 'Key characteristic: weld penetration depth 1.20 ± 0.20 mm'],
    ['M1 · CMU board assembly + screw fastening', 'Final torque 2.50 ± 0.25 N·m · spindle health from motor current'],
    ['M2 · 3D vision, gauging and weighing', 'Module height 108.00 ± 0.30 mm · structured-light scan'],
    ['M3 · End-of-line electrical test + DMC marking', 'HiPot, OCV and DC-IR 3.20 ± 0.80 mΩ · pogo-pin wear tracked by the AI']
  ];
  const [title, sub] = titles[i];
  const live = m ? ` · health ${Math.round(m.health * 100)} % · OEE ${Math.round(m.oee * 100)} %` : '';
  return { title, sub: sub + live };
}

/**
 * Tours return { pose, caption } for a tour time `t` (seconds). They may read the current frame
 * (e.g. pick a loaded mover, react to AI events) but stay deterministic for a given frame stream.
 */
export const TOURS = {
  overview: {
    duration: 30,
    shot(t, ctx) {
      const far = { pos: new THREE.Vector3(8.5, 6.8, 9.5), target: new THREE.Vector3(0, 0.6, 0) };
      const ov = overviewPose();
      if (t < 7) {
        return { pose: blendPose(far, ov, t / 7), caption: { title: 'EV battery module line · XTS transport · 4 robot cells', sub: 'Everything moving here is driven by the real C# PLC simulation – not a canned animation.' } };
      }
      if (t < 15) {
        ctx.followId ??= ctx.pickLoadedMover();
        const follow = moverFollowPose(ctx.fleet, ctx.followId);
        const pose = t < 8.5 ? blendPose(ov, follow, (t - 7) / 1.5) : follow;
        return { pose, caption: { title: 'No-overtaking linear transport', sub: 'Jerk-limited S-curves · 12 cm anti-collision gap · millimetre-accurate docking' } };
      }
      if (t < 22) {
        const cell = cellPose(ctx.busiestCell(), 0.85, 0.25 + (t - 15) * 0.05);
        const pose = t < 16.5 ? blendPose(moverFollowPose(ctx.fleet, ctx.followId ?? 0), cell, (t - 15) / 1.5) : cell;
        return { pose, caption: { title: 'Dual-gripper swap at the dock', sub: 'Raw module in, finished module out – the mover leaves after ~1.6 s, the robot loads the machine.' } };
      }
      if (t < 26.5) {
        const a = { pos: offsetPoint(ENTRY_ANGLE, 2.5, -1.5, 2.1), target: offsetPoint(ENTRY_ANGLE, 0.5, 0, 0.95) };
        const b = { pos: offsetPoint(EXIT_ANGLE + 6, 2.4, 1.4, 2.1), target: offsetPoint(EXIT_ANGLE + 4, 0.5, 0, 0.9) };
        return { pose: blendPose(a, b, (t - 22) / 4.5), caption: { title: 'Infeed of 12S cell stacks · outfeed of good modules and rejects', sub: 'Every module carries its full measurement genealogy into SQLite.' } };
      }
      const top = { pos: new THREE.Vector3(0.3, 7.2, 4.4), target: new THREE.Vector3(0, 0.6, 0) };
      const from = { pos: offsetPoint(EXIT_ANGLE + 6, 2.4, 1.4, 2.1), target: offsetPoint(EXIT_ANGLE + 4, 0.5, 0, 0.9) };
      return { pose: blendPose(from, top, (t - 26.5) / 3.5), caption: { title: 'Live KPIs: OEE, output, energy per module', sub: 'The AI autopilot sizes the work-in-process from the critical WIP W₀ = r_b × T₀.' } };
    }
  },

  stations: {
    duration: 40,
    shot(t, ctx) {
      const i = Math.min(3, Math.floor(t / 10));
      const local = t - i * 10;
      const zoom = lerp(1.25, 0.72, smooth(local / 7));
      const pose = cellPose(i, zoom, -0.35 + local * 0.07);
      if (local < 1.6 && i > 0) {
        const prev = cellPose(i - 1, 0.72, -0.35 + 10 * 0.07);
        const lifted = { pos: prev.pos.clone().lerp(pose.pos, 0.5).add(new THREE.Vector3(0, 1.6, 0)), target: prev.target.clone().lerp(pose.target, 0.5) };
        const k = local / 1.6;
        return { pose: k < 0.5 ? blendPose(prev, lifted, k * 2) : blendPose(lifted, pose, (k - 0.5) * 2), caption: stationCaption(i, ctx.frame?.machines?.[i]) };
      }
      return { pose, caption: stationCaption(i, ctx.frame?.machines?.[i]) };
    }
  },

  ai: {
    duration: 36,
    shot(t, ctx) {
      const m0 = ctx.frame?.machines?.[0];
      const ov = overviewPose();
      const cell = cellPose(0, 1.05, 0.15 + Math.sin(t * 0.12) * 0.25);
      let pose;
      if (t < 4) pose = blendPose(ov, cell, t / 4);
      else if (t > 31) pose = blendPose(cell, ov, (t - 31) / 5);
      else pose = cell;
      return { pose, caption: aiCaption(ctx, m0) };
    }
  }
};

function aiCaption(ctx, m) {
  if (!m) return null;
  const state = ctx.aiStory ??= { sawFault: false, sawAnomaly: false, sawMaint: false };
  if (m.fault2) state.sawFault = true;
  if (m.anomalyFlag) state.sawAnomaly = true;
  if (m.maint !== 'None') state.sawMaint = true;
  const rul = m.rul == null ? 'learning' : fmtDuration(m.rul);
  if (m.maint === 'InProgress') {
    return { title: 'Technician on the cell (lock-out/tag-out)', sub: `Protective window replaced, optics cleaned · ${Math.ceil(m.maintLeft)} s left · AI baselines will re-commission` };
  }
  if (m.maint === 'Pending') {
    return { title: 'Autopilot schedules predictive maintenance', sub: 'Cell drains its last module · release of new cell stacks is held · no breakdown, no scrap' };
  }
  if (state.sawMaint) {
    return { title: 'Back in production – zero breakdowns, zero scrap', sub: `Health ${Math.round(m.health * 100)} % · the planned 9 s stop replaced a ≈35 s breakdown` };
  }
  if (m.anomalyFlag) {
    return { title: 'Anomaly: laser output power residual out of family', sub: `Digital-twin residual + CUSUM · health ${Math.round(m.health * 100)} % · remaining useful life ${rul}` };
  }
  if (state.sawFault) {
    return { title: 'What-if: protective window contamination injected', sub: 'The AI gets no hint – it must find the fault from sensors and weld measurements alone.' };
  }
  return { title: 'M0 laser welder in normal production', sub: `Health ${Math.round(m.health * 100)} % · weld depth SPC in control` };
}

export class CameraDirector {
  constructor(camera, controls) {
    this.camera = camera;
    this.controls = controls;
    this.mode = 'overview';
    this.tour = null;
    this.tourTime = 0;
    this.followId = null;
    this.desired = overviewPose();
    this.ctx = {};
  }

  setPreset(name, fleet) {
    this.mode = name;
    this.tour = null;
    if (name === 'overview') this.desired = overviewPose();
    else if (/^m[0-3]$/.test(name)) this.desired = cellPose(Number(name[1]), 0.9, 0.3);
    else if (name === 'follow') this.followId = null;
    this.controls.enabled = name !== 'follow';
  }

  startTour(name) {
    this.mode = 'tour';
    this.tour = TOURS[name] ?? TOURS.overview;
    this.tourTime = 0;
    this.ctx = {};
    this.controls.enabled = false;
  }

  /** Interactive update (smoothly flies to the selected preset; user keeps orbit control). */
  update(dt, fleet, frame, ctxHelpers) {
    if (this.mode === 'tour') {
      this.tourTime += dt;
      const result = this.applyTour(this.tourTime % this.tour.duration, fleet, frame, ctxHelpers);
      return result;
    }

    if (this.mode === 'follow') {
      if (this.followId == null) this.followId = ctxHelpers.pickLoadedMover();
      const pose = moverFollowPose(fleet, this.followId);
      this.camera.position.lerp(pose.pos, 1 - Math.exp(-dt * 4));
      this.controls.target.lerp(pose.target, 1 - Math.exp(-dt * 6));
      this.camera.lookAt(this.controls.target);
      return null;
    }

    if (this.desired) {
      const k = 1 - Math.exp(-dt * 2.5);
      this.camera.position.lerp(this.desired.pos, k);
      this.controls.target.lerp(this.desired.target, k);
      if (this.camera.position.distanceTo(this.desired.pos) < 0.02) this.desired = null;
    }

    this.controls.update();
    return null;
  }

  /** Deterministic tour pose for capture: no smoothing state other than the tour context. */
  applyTour(t, fleet, frame, ctxHelpers) {
    Object.assign(this.ctx, ctxHelpers, { fleet, frame });
    const { pose, caption } = this.tour.shot(t, this.ctx);
    this.camera.position.copy(pose.pos);
    this.controls.target.copy(pose.target);
    this.camera.lookAt(pose.target);
    return caption;
  }
}
