const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const makeDocument = require('./test-dom.cjs');

const sourcePath = path.join(__dirname, 'index.html');
const targetPath = path.join(__dirname, 'en/index.html');

function replaceOnce(source, current, replacement) {
  assert.equal(source.split(current).length, 2, `Expected one occurrence of: ${current}`);
  return source.replace(current, replacement);
}

function build() {
  const source = fs.readFileSync(sourcePath, 'utf8');
  const code = source.match(/<script>([\s\S]*?)<\/script>/)[1];
  const document = makeDocument(source);
  const context = vm.createContext({
    window: { matchMedia: () => ({ matches: false, addEventListener() {} }), location: { href: '' } },
    document,
    setTimeout() { throw new Error('Unexpected autoplay'); },
    clearTimeout() {}
  });
  vm.runInContext(code, context);
  vm.runInContext("setLanguage('en')", context);

  const bodyStart = source.indexOf('<body>') + '<body>'.length;
  const scriptStart = source.indexOf('<script>', bodyStart);
  let output = source.slice(0, bodyStart) + '\n' + document.serializeBody() + '\n' + source.slice(scriptStart);
  const replacements = [
    ['<html lang="pt-BR">', '<html lang="en">'],
    ['<meta name="description" content="Da ideia direto ao texto. Transforme suas ideias em texto com a voz. Matraca: ditado local com Whisper, código aberto, para Windows e macOS.">', '<meta name="description" content="From thought straight to text. Turn your ideas into writing with your voice. Matraca: local, open-source dictation with Whisper for Windows and macOS.">'],
    ['<meta property="og:title" content="Matraca · Sua voz, por escrito.">', '<meta property="og:title" content="Matraca · Your voice, in writing">'],
    ['<meta property="og:description" content="Da ideia direto ao texto. Dite no ritmo das suas ideias, no Windows ou no Mac, com reconhecimento local e código aberto.">', '<meta property="og:description" content="From thought straight to text. Dictate at the pace of your ideas, on Windows or Mac, with local speech recognition and open-source software.">'],
    ['<meta property="og:locale" content="pt_BR">', '<meta property="og:locale" content="en_US">'],
    ['<meta property="og:locale:alternate" content="en_US">', '<meta property="og:locale:alternate" content="pt_BR">'],
    ['<link rel="canonical" href="https://fabriciocasali.github.io/matraca/">', '<link rel="canonical" href="https://fabriciocasali.github.io/matraca/en/">'],
    ['<link rel="icon" type="image/svg+xml" href="favicon.svg">', '<link rel="icon" type="image/svg+xml" href="../favicon.svg">'],
    ['<meta property="og:url" content="https://fabriciocasali.github.io/matraca/">', '<meta property="og:url" content="https://fabriciocasali.github.io/matraca/en/">'],
    ['<title>Matraca · Sua voz, por escrito</title>', '<title>Matraca · Your voice, in writing</title>'],
    ['<option value="en" lang="en">English</option>', '<option value="en" lang="en" selected="selected">English</option>']
  ];
  for (const [current, replacement] of replacements) output = replaceOnce(output, current, replacement);
  return output;
}

function generate(check = false) {
  const output = build();
  if (check) {
    assert.ok(fs.existsSync(targetPath), 'Missing generated English page');
    assert.equal(fs.readFileSync(targetPath, 'utf8'), output, 'English page is out of date');
    return;
  }
  fs.mkdirSync(path.dirname(targetPath), { recursive: true });
  fs.writeFileSync(targetPath, output);
}

module.exports = { build, generate };
if (require.main === module) generate(process.argv.includes('--check'));
