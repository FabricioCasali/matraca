const assert = require('node:assert/strict');
const { renderPage, resolveReleaseData } = require('./build-site.cjs');
const fs = require('node:fs');

const releases = [
  {
    tag_name: 'v2.0.1-macos-preview.1',
    prerelease: true,
    draft: false,
    html_url: 'https://github.com/FabricioCasali/matraca/releases/tag/v2.0.1-macos-preview.1',
    assets: [{ name: 'Matraca-2.0.1-macos-arm64.zip', browser_download_url: 'https://github.com/FabricioCasali/matraca/releases/download/v2.0.1-macos-preview.1/Matraca-2.0.1-macos-arm64.zip' }]
  },
  {
    tag_name: 'v2.0.1',
    prerelease: false,
    draft: false,
    html_url: 'https://github.com/FabricioCasali/matraca/releases/tag/v2.0.1',
    assets: [{ name: 'matraca-setup-2.0.1.exe', browser_download_url: 'https://github.com/FabricioCasali/matraca/releases/download/v2.0.1/matraca-setup-2.0.1.exe' }]
  }
];
const release = resolveReleaseData(releases);
assert.equal(release.version, '2.0.1');
assert.equal(release.macVersion, '2.0.1-macos-preview.1');

for (const file of ['index.html', 'en/index.html']) {
  const source = fs.readFileSync(require('node:path').join(__dirname, file), 'utf8');
  const output = renderPage(source, release);
  assert.ok(output.includes('v2.0.1'));
  assert.ok(output.includes('macOS preview'));
  assert.ok(output.includes('v2.0.1-macos-preview.1'));
  assert.ok(output.includes('matraca-setup-2.0.1.exe'));
  assert.ok(!output.includes('v2.0.0'));
  assert.ok(!/__([A-Z_]+)__/.test(output));
}
console.log('PASS: site resolves the latest stable Windows release and macOS preview without stale version tokens.');
