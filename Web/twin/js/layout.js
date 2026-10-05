// Shared line geometry (metres, Y up). Mirrors the C# engine:
// mover positions are degrees around a 6 m stadium loop, machines dock at 45/135/225/315°,
// the entry (cell-stack load) is at 205° and the exit (module unload) at 0°.
import * as THREE from 'three';

export const TRACK_LENGTH = 6.0;
export const RADIUS = 0.55;
export const STRAIGHT = (TRACK_LENGTH - 2 * Math.PI * RADIUS) / 2;
export const TRACK_TOP = 0.95;          // height of the mover top plate
export const TRACK_HALF_WIDTH = 0.035;  // motor module thickness / 2
export const MACHINE_ANGLES = [45, 135, 225, 315];
export const ENTRY_ANGLE = 205;
export const EXIT_ANGLE = 0;
export const CELL_OFFSET = 1.12;        // dock → cell centre along the outward normal

/** Position, tangent and outward normal on the loop for a mover angle in degrees. */
export function pathAt(deg) {
  const d = ((deg % 360) + 360) % 360;
  const s = (d / 360) * TRACK_LENGTH;
  const L = STRAIGHT, R = RADIUS, A = Math.PI * R;
  if (s < L) {
    return { x: -L / 2 + s, z: -R, tx: 1, tz: 0, nx: 0, nz: -1 };
  }
  if (s < L + A) {
    const t = (s - L) / R;
    return { x: L / 2 + R * Math.sin(t), z: -R * Math.cos(t), tx: Math.cos(t), tz: Math.sin(t), nx: Math.sin(t), nz: -Math.cos(t) };
  }
  if (s < 2 * L + A) {
    const b = s - L - A;
    return { x: L / 2 - b, z: R, tx: -1, tz: 0, nx: 0, nz: 1 };
  }
  const t = (s - 2 * L - A) / R;
  return { x: -L / 2 - R * Math.sin(t), z: R * Math.cos(t), tx: -Math.cos(t), tz: -Math.sin(t), nx: -Math.sin(t), nz: Math.cos(t) };
}

/** Yaw (rotation about Y) so that local +X points along the tangent. */
export function yawOf(p) {
  return Math.atan2(-p.tz, p.tx);
}

/** Point offset from the loop: `out` along the outward normal, `along` the tangent, at height y. */
export function offsetPoint(deg, out, along = 0, y = 0) {
  const p = pathAt(deg);
  return new THREE.Vector3(p.x + p.nx * out + p.tx * along, y, p.z + p.nz * out + p.tz * along);
}

/** Stadium outline as a THREE.Shape (XZ plane mapped to shape XY with y = -z). */
export function stadiumShape(radius, straight = STRAIGHT) {
  const shape = new THREE.Shape();
  const L = straight / 2;
  shape.moveTo(-L, radius);
  shape.lineTo(L, radius);
  shape.absarc(L, 0, radius, Math.PI / 2, -Math.PI / 2, true);
  shape.lineTo(-L, -radius);
  shape.absarc(-L, 0, radius, -Math.PI / 2, Math.PI / 2, true);
  return shape;
}

export function stadiumPath(radius, straight = STRAIGHT) {
  const path = new THREE.Path();
  const L = straight / 2;
  path.moveTo(-L, radius);
  path.lineTo(L, radius);
  path.absarc(L, 0, radius, Math.PI / 2, -Math.PI / 2, true);
  path.lineTo(-L, -radius);
  path.absarc(-L, 0, radius, -Math.PI / 2, Math.PI / 2, true);
  return path;
}

export const lerp = (a, b, t) => a + (b - a) * t;
export const clamp01 = (t) => Math.min(1, Math.max(0, t));
export const smooth = (t) => { t = clamp01(t); return t * t * (3 - 2 * t); };

/** Shortest-path interpolation of loop angles (handles the 360° wrap). */
export function lerpAngle(a, b, t) {
  let d = b - a;
  if (d > 180) d -= 360;
  if (d < -180) d += 360;
  return (a + d * t + 360) % 360;
}
