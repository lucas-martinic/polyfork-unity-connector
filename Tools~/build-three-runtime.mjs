/* Builds the JavaScript runtime a Polyfork asset module needs, as one script.
 *
 *   node Tools~/build-three-runtime.mjs [path/to/threejs-3d-assets]
 *
 * WHY THIS EXISTS
 *
 * Local baking evaluates an asset's own module in the editor. The module is an ES module
 * importing three and, in 518 of 578 published cases, `three/addons/utils/BufferGeometryUtils.js`.
 * PolyforkModuleTransform strips the import lines and evaluates the body, so every name those
 * imports would have bound has to already exist as a global before the body runs.
 *
 * The bundle this replaces was hand-trimmed, and trimmed far past what the catalogue uses.
 * Measured by running every published module against it: 43 of 578 built, 535 threw. The
 * failures were not exotic —
 *
 *     366  mergeGeometries is not defined        (the addons import, dropped and never bound)
 *      60  THREE.Matrix4 is not a constructor
 *      41  THREE.Bone is not a constructor
 *      28  THREE.Path is not a constructor
 *      18  THREE.SphereGeometry is not a constructor
 *
 * — so local baking almost never ran, and virtually every asset silently fell back to the
 * server. That is what "some assets rebuild while I drag and some only move when I let go"
 * was: the ones that worked were the 7% whose modules happened to fit.
 *
 * With this runtime, 576 of 578 build.
 *
 * ON SIZE. 736 KB minified against 342 KB before. It is Editor-only and never enters a player
 * build, so the trade is 394 KB of editor memory for the difference between a feature that
 * works and one that works for one asset in fourteen. Do not re-trim this by hand; if it needs
 * to shrink, shrink it by measuring what the catalogue imports, the way the numbers above were
 * produced.
 */

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

const site = process.argv[2] ?? '/root/apps/threejs-3d-assets';
const vendor = path.join(site, 'public/vendor/three');
const out = path.join(import.meta.dirname, '../Editor/JS/three-runtime.txt');

for (const p of [
  path.join(vendor, 'build/three.module.js'),
  path.join(vendor, 'examples/jsm/utils/BufferGeometryUtils.js'),
]) {
  if (!fs.existsSync(p)) {
    console.error(`missing: ${p}\nPass the path to the threejs-3d-assets checkout as argv[2].`);
    process.exit(1);
  }
}

/* The entry mirrors what a module body expects to find already defined: THREE as a namespace,
 * and the addon helpers as BARE names, because that is how the body refers to them once its
 * import line has been stripped. */
const entry = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'polyfork-three-')), 'entry.mjs');
fs.writeFileSync(entry, `
import * as THREE from 'three';
import * as BufferGeometryUtils from 'three/addons/utils/BufferGeometryUtils.js';
import { ConvexGeometry } from 'three/addons/geometries/ConvexGeometry.js';

globalThis.THREE = THREE;
for (const [name, value] of Object.entries(BufferGeometryUtils)) globalThis[name] = value;
globalThis.ConvexGeometry = ConvexGeometry;
`);

execFileSync('npx', [
  '--yes', 'esbuild', entry,
  '--bundle', '--format=iife', '--platform=neutral', '--minify',
  `--alias:three=${path.join(vendor, 'build/three.module.js')}`,
  `--alias:three/addons=${path.join(vendor, 'examples/jsm')}`,
  `--outfile=${out}`,
  '--log-level=error',
], { stdio: 'inherit' });

console.log(`${out}  (${Math.round(fs.statSync(out).size / 1024)} KB)`);
console.log('Verify with Tools~/check-modules.mjs before shipping it.');
