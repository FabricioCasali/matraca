// resvg rasterizes the unmodified official SVG. ICO contains PNG frames (Vista+).
const fs = require('node:fs');
const path = require('node:path');
const { Resvg } = require('@resvg/resvg-js');
const root = path.resolve(__dirname, '../..');
const brand = path.join(root, 'design/assets/brand');
const output = path.join(root, 'Matraca.Windows');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const sources = { 'app.ico': 'matraca-03a-app-olive-light.svg' };
for (const theme of ['light', 'dark']) {
  for (const palette of ['olive', 'ochre', 'terracotta', 'plum', 'teal'])
    sources[`Icons/app-${palette}-${theme}.ico`] = `matraca-03a-app-${palette}-${theme}.svg`;
  sources[`Icons/idle-${theme}.ico`] = `matraca-03a-symbol-${theme}.svg`;
  for (const state of ['recording', 'busy', 'error'])
    sources[`Icons/${state}-${theme}.ico`] = `matraca-03a-tray-${state}-${theme}.svg`;
}

function render(svg, size) {
  return new Resvg(svg, { fitTo: { mode: 'width', value: size }, font: { loadSystemFonts: false } }).render().asPng();
}

function encode(svg) {
  const frames = sizes.map(size => render(svg, size));
  const directory = Buffer.alloc(6 + 16 * sizes.length);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  frames.forEach((frame, i) => {
    const entry = 6 + 16 * i;
    directory[entry] = directory[entry + 1] = sizes[i] % 256;
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(frame.length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += frame.length;
  });
  return Buffer.concat([directory, ...frames]);
}

function generate(check) {
  for (const [file, source] of Object.entries(sources)) {
    const ico = encode(fs.readFileSync(path.join(brand, source)));
    const target = path.join(output, file);
    if (check) {
      if (!fs.existsSync(target) || !ico.equals(fs.readFileSync(target)))
        throw Error(`ICO diverge do SVG oficial: ${file}`);
    } else {
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.writeFileSync(target, ico);
    }
  }
  // Superseded microphone assets must not remain in future build/publish inputs.
  for (const legacy of ['rec.ico', 'busy.ico']) {
    const target = path.join(output, legacy);
    if (check) {
      if (fs.existsSync(target)) throw Error(`Icone legado ainda presente: ${legacy}`);
    } else fs.rmSync(target, { force: true });
  }
  console.log(`${Object.keys(sources).length} ICOs oficiais ${check ? 'conferidos' : 'gerados'}.`);
}

module.exports = { sources, sizes, render, encode, generate };
if (require.main === module) generate(process.argv.includes('--check'));
