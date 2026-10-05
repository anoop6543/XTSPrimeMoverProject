// 6-axis cell robots (pedestal-mounted). Pose is solved with analytic two-link IK so the tool flange
// follows the real robot state machine: pick from mover → move → place into machine, pick from
// outfeed nest → move → place on mover, plus the dual-gripper swap.
import * as THREE from 'three';
import { materials } from './factory.js';
import { lerp, smooth } from './layout.js';

const L1 = 0.52;   // upper arm (≈1.1 m reach class robot)
const L2 = 0.48;   // forearm
const TOOL = 0.16; // wrist → gripper bar
const SHOULDER_HEIGHT = 0.86;
const CUP_TO_PART_BOTTOM = 0.135; // cup face + module height: the module hangs below the cups
const TCP_OFFSET = TOOL - 0.03 + CUP_TO_PART_BOTTOM; // wrist centre → bottom of a held module

export class CellRobot {
  constructor(parent, basePosition, homeYaw) {
    this.root = new THREE.Group();
    this.root.position.copy(basePosition);
    parent.add(this.root);
    this.homeYaw = homeYaw;

    const pedestal = new THREE.Mesh(new THREE.CylinderGeometry(0.11, 0.14, 0.62, 28), materials.cabinetDark);
    pedestal.position.y = 0.31;
    pedestal.castShadow = true;
    this.root.add(pedestal);
    const baseRing = new THREE.Mesh(new THREE.CylinderGeometry(0.13, 0.13, 0.06, 32), materials.robotGrey);
    baseRing.position.y = 0.65;
    this.root.add(baseRing);

    this.turret = new THREE.Group();
    this.turret.position.y = 0.68;
    this.root.add(this.turret);
    const turretBody = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.12, 0.16, 28), materials.robotOrange);
    turretBody.position.y = 0.08;
    turretBody.castShadow = true;
    this.turret.add(turretBody);

    this.shoulder = new THREE.Group();
    this.shoulder.position.y = SHOULDER_HEIGHT - 0.68;
    this.turret.add(this.shoulder);
    const shoulderHub = new THREE.Mesh(new THREE.CylinderGeometry(0.075, 0.075, 0.17, 24), materials.robotGrey);
    shoulderHub.rotation.x = Math.PI / 2;
    this.shoulder.add(shoulderHub);
    const upper = new THREE.Mesh(new THREE.BoxGeometry(L1, 0.09, 0.1), materials.robotOrange);
    upper.position.x = L1 / 2;
    upper.castShadow = true;
    this.shoulder.add(upper);

    this.elbow = new THREE.Group();
    this.elbow.position.x = L1;
    this.shoulder.add(this.elbow);
    const elbowHub = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 0.14, 24), materials.robotGrey);
    elbowHub.rotation.x = Math.PI / 2;
    this.elbow.add(elbowHub);
    const fore = new THREE.Mesh(new THREE.BoxGeometry(L2, 0.07, 0.075), materials.robotOrange);
    fore.position.x = L2 / 2;
    fore.castShadow = true;
    this.elbow.add(fore);
    const motor = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.035, 0.1, 16), materials.robotGrey);
    motor.rotation.z = Math.PI / 2;
    motor.position.set(-0.04, 0, 0);
    this.elbow.add(motor);

    this.wrist = new THREE.Group();
    this.wrist.position.x = L2;
    this.elbow.add(this.wrist);
    const wristHub = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 0.09, 20), materials.robotGrey);
    wristHub.rotation.x = Math.PI / 2;
    this.wrist.add(wristHub);

    // Tool points along wrist +X; wrist rotation keeps it vertical (pointing down).
    this.tool = new THREE.Group();
    this.wrist.add(this.tool);
    const flange = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.06, 16), materials.steel);
    flange.rotation.z = Math.PI / 2;
    flange.position.x = 0.04;
    this.tool.add(flange);

    // Dual gripper: two vacuum plates side by side (gripper A / gripper B)
    this.gripperBar = new THREE.Group();
    this.gripperBar.position.x = TOOL - 0.03;
    this.tool.add(this.gripperBar);
    const bar = new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.04, 0.3), materials.aluminiumDark);
    bar.position.z = 0.075;
    this.gripperBar.add(bar);
    this.tcpA = new THREE.Group();
    this.tcpB = new THREE.Group();
    // Gripper A sits on the arm plane (lands exactly on the target); gripper B is offset for swaps.
    for (const [tcp, z] of [[this.tcpA, 0], [this.tcpB, 0.15]]) {
      const cup = new THREE.Mesh(new THREE.BoxGeometry(0.025, 0.12, 0.09), materials.rubber);
      cup.position.set(0.025, 0, z);
      this.gripperBar.add(cup);
      tcp.position.set(CUP_TO_PART_BOTTOM, 0, z);
      // part frame: the module hangs with its top against the cups → rotate so module +Y points back to the robot
      tcp.rotation.z = Math.PI / 2;
      this.gripperBar.add(tcp);
    }

    // Vacuum status LED
    this.ledMat = new THREE.MeshStandardMaterial({ color: 0x111111, emissive: 0x22c55e, emissiveIntensity: 2 });
    const led = new THREE.Mesh(new THREE.SphereGeometry(0.012, 12, 8), this.ledMat);
    led.position.set(0.04, 0.05, 0);
    this.tool.add(led);

    this.current = new THREE.Vector3();
    this.home = new THREE.Vector3();
    this.initialized = false;
  }

  /** Solve the arm so the suction cups touch `target` (world) with the tool vertical. */
  solve(target) {
    const local = this.root.worldToLocal(target.clone());
    const yaw = Math.atan2(-local.z, local.x);
    this.turret.rotation.y = yaw;
    const r = Math.max(0.18, Math.hypot(local.x, local.z));
    const wristY = local.y + TCP_OFFSET;
    const h = wristY - SHOULDER_HEIGHT;
    const d = Math.min(L1 + L2 - 1e-3, Math.max(Math.abs(L1 - L2) + 1e-3, Math.hypot(r, h)));
    const cosE = (d * d - L1 * L1 - L2 * L2) / (2 * L1 * L2);
    const e = Math.acos(Math.max(-1, Math.min(1, cosE)));
    const a1 = Math.atan2(h, r) + Math.atan2(L2 * Math.sin(e), L1 + L2 * Math.cos(e));
    const a2 = a1 - e;
    this.shoulder.rotation.z = a1;
    this.elbow.rotation.z = -e;
    this.wrist.rotation.z = -Math.PI / 2 - a2;
  }

  /**
   * @param {object|null} state robot frame data {st, p}
   * @param {{mover:THREE.Vector3, machine:THREE.Vector3, nest:THREE.Vector3}} points world targets
   */
  update(state, points, dt) {
    const above = (v, dy) => v.clone().add(new THREE.Vector3(0, dy, 0));
    const home = above(points.mover.clone().lerp(points.machine, 0.5), 0.32);
    let target = home;
    const st = state?.st ?? 'Idle';
    const p = smooth(state?.p ?? 0);
    const dip = (v) => above(v, 0.1 * (1 - Math.sin(Math.PI * Math.min(1, p))) + 0.012);
    const arc = (a, b) => {
      const t = p;
      const v = a.clone().lerp(b, t);
      v.y += 0.14 * Math.sin(Math.PI * t);
      return above(v, 0.1);
    };

    switch (st) {
      case 'PickingFromMover': target = dip(points.mover); break;
      case 'MovingToMachine': target = arc(points.mover, points.machine); break;
      case 'PlacingInMachine': target = dip(points.machine); break;
      case 'PickingFromMachine': target = dip(points.nest); break;
      case 'MovingToMover': target = arc(points.nest, points.mover); break;
      case 'PlacingOnMover':
        target = state?.staged ? above(points.mover, 0.2) : dip(points.mover);
        break;
      case 'Idle':
      default:
        target = home;
    }

    if (!this.initialized) {
      this.current.copy(target);
      this.initialized = true;
    }

    // Critically damped follow so state switches never teleport the arm.
    const k = 1 - Math.exp(-dt * 14);
    this.current.lerp(target, k);
    this.solve(this.current);

    const holding = !!state?.part;
    this.ledMat.emissive.setHex(holding ? 0x38bdf8 : 0x22c55e);
  }
}

export { lerp };
