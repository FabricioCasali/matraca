const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const REPOSITORY = 'https://github.com/FabricioCasali/matraca';
const SOURCE_DIR = __dirname;

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function isRelease(release) {
  return release && !release.draft && Array.isArray(release.assets);
}

function asset(release, predicate) {
  return release.assets.find(item => predicate(item.name));
}

function resolveReleaseData(input) {
  const releases = Array.isArray(input) ? input.filter(isRelease) : [input].filter(isRelease);
  const stable = releases.find(release => !release.prerelease && asset(release, name => name.endsWith('.exe')));
  assert.ok(stable, 'The release feed has no stable Windows release with an installer');

  const windowsAsset = asset(stable, name => name.endsWith('.exe'));
  const mac = releases.find(release => asset(release, name => /macos-arm64\.zip$/i.test(name)));
  const macAsset = mac && asset(mac, name => /macos-arm64\.zip$/i.test(name));

  return {
    version: stable.tag_name.replace(/^v/, ''),
    windowsReleaseUrl: stable.html_url || `${REPOSITORY}/releases/tag/${stable.tag_name}`,
    windowsDownloadUrl: windowsAsset.browser_download_url,
    macReleaseUrl: mac ? (mac.html_url || `${REPOSITORY}/releases/tag/${mac.tag_name}`) : `${REPOSITORY}/releases`,
    macDownloadUrl: macAsset ? macAsset.browser_download_url : null,
    macVersion: mac ? mac.tag_name.replace(/^v/, '') : null,
    macPreview: Boolean(mac && mac.prerelease)
  };
}

function replaceMacFaq(source, faq) {
  return source.replace(
    /(<details data-od-id="faq-platform">[\s\S]*?<p>)[\s\S]*?(<\/p>)/,
    `$1${faq}$2`
  );
}

function renderPage(source, release) {
  const english = /<html lang="en">/.test(source);
  const macAvailable = Boolean(release.macVersion);
  const platformStatusPt = macAvailable
    ? (release.macPreview ? 'macOS preview disponível' : 'macOS disponível')
    : 'macOS em preparação';
  const platformStatusEn = macAvailable
    ? (release.macPreview ? 'macOS preview available' : 'macOS available')
    : 'macOS coming soon';
  const faqPt = macAvailable
    ? `${release.macPreview ? 'O instalador público está disponível para Windows x64. Um preview público para macOS Apple Silicon está disponível' : 'O instalador público está disponível para Windows x64. Um pacote público para macOS Apple Silicon está disponível'} na <a href="${release.macReleaseUrl}" target="_blank" rel="noopener">página de versões do GitHub</a>. Ele é assinado apenas com a identidade local de desenvolvimento &quot;Matraca Dev&quot;; não tem certificado oficial Apple nem notarização. Atualizações automáticas ainda não estão disponíveis. Não há versão Linux anunciada.`
    : `O instalador público está disponível para Windows x64. O pacote macOS Apple Silicon está em preparação e ainda não está disponível na <a href="${release.macReleaseUrl}" target="_blank" rel="noopener">página de versões do GitHub</a>. Não há versão Linux anunciada.`;
  const faqEn = macAvailable
      ? `${release.macPreview ? 'A public installer is available for Windows x64. A public preview for Apple Silicon macOS is available' : 'A public installer is available for Windows x64. A public Apple Silicon macOS package is available'} on the <a href="${release.macReleaseUrl}" target="_blank" rel="noopener">GitHub releases page</a>. It is signed only with the local development identity &quot;Matraca Dev&quot;; it has no Apple distribution certificate or notarization. Automatic updates are not available yet. No Linux release has been announced.`
      : `A public installer is available for Windows x64. The Apple Silicon macOS package is being prepared and is not yet available on the <a href="${release.macReleaseUrl}" target="_blank" rel="noopener">GitHub releases page</a>. No Linux release has been announced.`;
  const oldPreviewFaqPt = 'O instalador público está disponível para Windows x64. Um preview público para macOS Apple Silicon está disponível na <a href="https://github.com/FabricioCasali/matraca/releases/tag/v2.0.1-macos-preview.1" target="_blank" rel="noopener">página de versões do GitHub</a>. Ele é assinado apenas com a identidade local de desenvolvimento &quot;Matraca Dev&quot;; não tem certificado oficial Apple nem notarização. Não há versão Linux anunciada.';
  const oldPreviewFaqEn = 'A public installer is available for Windows x64. A public preview for Apple Silicon macOS is available on the <a href="https://github.com/FabricioCasali/matraca/releases/tag/v2.0.1-macos-preview.1" target="_blank" rel="noopener">GitHub releases page</a>. It is signed only with the local development identity &quot;Matraca Dev&quot;; it has no Apple distribution certificate or notarization. No Linux release has been announced.';
  const oldEnglishMacFaq = 'A public installer is available for Windows x64. The macOS Apple Silicon package is being prepared and is not yet available on the <a href="https://github.com/FabricioCasali/matraca/releases" target="_blank" rel="noopener">GitHub releases page</a>. Follow releases for availability and installation instructions. No Linux release has been announced.';

  let output = source
    .replaceAll(oldPreviewFaqPt, faqPt)
    .replaceAll(oldPreviewFaqEn, faqEn)
    .replace(/Matraca \d+\.\d+\.\d+(?=<\/strong>)/g, `Matraca ${release.version}`)
    .replace(/href="https:\/\/github\.com\/FabricioCasali\/matraca\/releases\/download\/[^\"]+"(?=[^>]*data-od-id="download-windows")/, `href="${release.windowsDownloadUrl}"`)
    .replace(/href="https:\/\/github\.com\/FabricioCasali\/matraca\/releases(?:\/tag\/[^\"]+)?"(?=[^>]*data-od-id="download-mac")/, `href="${release.macReleaseUrl}"`)
    .replace(/href="https:\/\/github\.com\/FabricioCasali\/matraca\/releases\/tag\/[^\"]+"/g, `href="${release.windowsReleaseUrl}"`)
    .replace(/Windows · (?:macOS preview disponível|macOS em preparação)/g, `Windows · ${platformStatusPt}`)
    .replace(/Windows · (?:macOS preview available|macOS coming soon)/g, `Windows · ${platformStatusEn}`)
    .replace(/Windows x64 · Código aberto · Mac em preparação/g, `Windows x64 · Código aberto · ${platformStatusPt}`)
    .replace(/Windows x64 · Open source · Mac coming soon/g, `Windows x64 · Open source · ${platformStatusEn}`)
    .replaceAll('macOS preview disponível', platformStatusPt)
    .replaceAll('macOS preview available', platformStatusEn)
    .replace(oldEnglishMacFaq, faqEn);

  output = replaceMacFaq(output, english ? faqEn : faqPt);
  return output
    .replaceAll('__MATRACA_VERSION__', release.version)
    .replaceAll('__WINDOWS_DOWNLOAD_URL__', release.windowsDownloadUrl)
    .replaceAll('__WINDOWS_RELEASE_URL__', release.windowsReleaseUrl)
    .replaceAll('__MAC_RELEASE_URL__', release.macReleaseUrl)
    .replaceAll('__PLATFORM_STATUS_PT__', platformStatusPt)
    .replaceAll('__PLATFORM_STATUS_EN__', platformStatusEn)
    .replaceAll('__PLATFORM_FAQ_PT__', faqPt)
    .replaceAll('__PLATFORM_FAQ_EN__', faqEn);
}

function buildSite(releaseInput, outputDir) {
  const release = resolveReleaseData(releaseInput);
  fs.mkdirSync(outputDir, { recursive: true });
  fs.mkdirSync(path.join(outputDir, 'en'), { recursive: true });
  fs.writeFileSync(path.join(outputDir, 'index.html'), renderPage(fs.readFileSync(path.join(SOURCE_DIR, 'index.html'), 'utf8'), release));
  fs.writeFileSync(path.join(outputDir, 'en/index.html'), renderPage(fs.readFileSync(path.join(SOURCE_DIR, 'en/index.html'), 'utf8'), release));
  fs.copyFileSync(path.join(SOURCE_DIR, 'favicon.svg'), path.join(outputDir, 'favicon.svg'));
  fs.writeFileSync(path.join(outputDir, '.nojekyll'), '');
  return release;
}

if (require.main === module) {
  const jsonIndex = process.argv.indexOf('--release-json');
  const outputIndex = process.argv.indexOf('--output');
  assert.ok(jsonIndex >= 0 && process.argv[jsonIndex + 1], 'Usage: node site/build-site.cjs --release-json releases.json --output _site');
  assert.ok(outputIndex >= 0 && process.argv[outputIndex + 1], 'Usage: node site/build-site.cjs --release-json releases.json --output _site');
  const release = buildSite(readJson(process.argv[jsonIndex + 1]), process.argv[outputIndex + 1]);
  console.log(`Built site for Windows ${release.version}${release.macVersion ? ` and macOS ${release.macVersion}` : ''}`);
}

module.exports = { buildSite, renderPage, resolveReleaseData };
