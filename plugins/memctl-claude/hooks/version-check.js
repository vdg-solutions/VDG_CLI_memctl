#!/usr/bin/env node
// memctl — version drift guard
// Compares plugin version against installed binary at SessionStart.
// Warns when binary is older than plugin so model can tell user to update.

const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

// Resolve plugin version from plugin.json
const pluginRoot = process.env.CLAUDE_PLUGIN_ROOT;
if (!pluginRoot) { process.exit(0); }

let pluginVer;
try {
  const pp = path.join(pluginRoot, '.claude-plugin', 'plugin.json');
  pluginVer = JSON.parse(fs.readFileSync(pp, 'utf8')).version;
} catch (_) {
  process.exit(0);
}

// Resolve binary version
let binaryVer;
try {
  const out = execSync('memctl --version', { encoding: 'utf8', timeout: 5000 }).trim();
  const m = out.match(/^(\d+\.\d+\.\d+)/);
  binaryVer = m ? m[1] : null;
} catch (_) {
  process.exit(0);
}

if (!binaryVer || !pluginVer) { process.exit(0); }

// Semver compare
const [bM, bm, bp] = binaryVer.split('.').map(Number);
const [pM, pm, pp] = pluginVer.split('.').map(Number);
const stale = bM < pM || (bM === pM && (bm < pm || (bm === pm && bp < pp)));

if (!stale) { process.exit(0); }

process.stdout.write(
  'VERSION DRIFT: plugin v' + pluginVer + ' > binary v' + binaryVer + '. ' +
  'Tell user: "dotnet tool update -g memctl"'
);
