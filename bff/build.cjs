const fs = require('node:fs');
const path = require('node:path');
const { buildSync } = require('esbuild');

const entryPoints = fs.readdirSync(path.join(__dirname, 'src'), { withFileTypes: true })
  .filter(entry => entry.isDirectory() && fs.existsSync(path.join(__dirname, 'src', entry.name, 'index.ts')))
  .map(entry => path.join('src', entry.name, 'index.ts'));
if (!entryPoints.length) throw new Error('No managed BFF handlers found');
buildSync({
  absWorkingDir: __dirname, entryPoints, bundle: true,
  platform: 'node', target: 'node20', outbase: 'src', outdir: 'dist', logLevel: 'info',
});
for (const entry of entryPoints) {
  const output = path.join(__dirname, 'dist', path.basename(path.dirname(entry)), 'index.js');
  if (!fs.existsSync(output)) throw new Error('Missing managed BFF bundle: ' + output);
}
