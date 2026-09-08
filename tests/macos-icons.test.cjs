const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { macOutput, macSource, icnsFrames, render, generate } = require('../tools/icons/make-icons.cjs');

const root = path.resolve(__dirname, '..');
generate(true);

const icns = fs.readFileSync(macOutput);
const svg = fs.readFileSync(path.join(root, 'design/assets/brand', macSource));
assert.equal(icns.subarray(0, 4).toString('ascii'), 'icns');
assert.equal(icns.readUInt32BE(4), icns.length);

let offset = 8;
for (const [expectedType, size] of icnsFrames) {
  const type = icns.subarray(offset, offset + 4).toString('ascii');
  const length = icns.readUInt32BE(offset + 4);
  const png = icns.subarray(offset + 8, offset + length);
  assert.equal(type, expectedType);
  assert.equal(png.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
  assert.equal(png.readUInt32BE(16), size);
  assert.equal(png.readUInt32BE(20), size);
  assert.deepEqual(png, render(svg, size), `${type} ${size}px preserves official SVG`);
  offset += length;
}
assert.equal(offset, icns.length);

const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const plist = read('Matraca.Mac/Info.plist');
assert.match(plist, /<key>CFBundleIconFile<\/key>\s*<string>Matraca\.icns<\/string>/);
assert.match(read('Matraca.Mac/pack.sh'), /Resources\/Matraca\.icns/);
console.log(`${icnsFrames.length} ICNS frames and macOS bundle contract: OK`);
