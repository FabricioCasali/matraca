const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const assert=require('node:assert/strict');
const html=fs.readFileSync(path.join(__dirname,'..','matraca-identidade.html'),'utf8');
const code=html.match(/<script>([\s\S]*?)<\/script>/)[1];
new vm.Script(code);
const nodes=new Map(),storage=new Map();
const node=id=>{if(!nodes.has(id))nodes.set(id,{innerHTML:'',textContent:'',value:'',dataset:{},addEventListener(){},focus(){}});return nodes.get(id);};
const context=vm.createContext({document:{getElementById:node,documentElement:{dataset:{},style:{setProperty(){}}},addEventListener(){},querySelector(){return node('selected');}},localStorage:{getItem:key=>storage.get(key),setItem:(key,val)=>storage.set(key,val)},setTimeout,console});
vm.runInContext(code,context);
const run=value=>vm.runInContext(value,context);
assert.equal(run('choice.id'),'monogram-speech');
assert.deepEqual(Array.from(run('Object.keys(concepts).filter(id=>id.startsWith("monogram"))')),['monogram','monogram-speech','monogram-rhythm','monogram-script']);
let variants=0;
for(const id of ['monogram','monogram-speech','monogram-rhythm','monogram-script'])for(const theme of ['light','dark'])for(const hue of [125,85,35,325,175]){
 run(`choice.id='${id}';choice.theme='${theme}';choice.hue=${hue};render()`);
 assert.ok(node('selected-title').textContent.length>0);
 assert.equal((node('directions').innerHTML.match(/aria-pressed="true"/g)||[]).length,1);
 for(const size of [16,20,24,32,48])assert.ok(node('sizes').innerHTML.includes(`width="${size}" height="${size}"`));
 for(const variant of ['symbol','app','recording','busy','error']){
  const result=run(`exportSvg('${variant}')`);assert.ok(result.startsWith('<svg '));assert.ok(result.endsWith('</svg>'));
  assert.ok(!/var\(|currentColor|<text|NaN|undefined/.test(result),'Portable vectors without external tokens or fonts');
  assert.ok(result.includes('<title>Matraca'));
  const stack=[];
  for(const tag of result.matchAll(/<(\/?)(\w+)\b[^>]*>/g)){if(tag[0].endsWith('/>'))continue;if(tag[1])assert.equal(stack.pop(),tag[2]);else stack.push(tag[2]);}
  assert.equal(stack.length,0);variants++;
 }
}
assert.equal(variants,200);
assert.ok(!/fetch\(|https?:\/\/(?!www.w3.org)/.test(code));
assert.ok(storage.has('matraca-brand-studies'));
console.log('PASS: original monogram plus three refinements, ten palettes, five size previews and 200 SVG export combinations. Optical evaluation remains visual.');
for(const mode of ['idle','listening','loading','done']){run(`motion.mode='${mode}';renderMotion()`);assert.equal(node('motion-stage').dataset.mode,mode);assert.ok(node('motion-title').textContent);assert.ok(node('motion-stage').innerHTML.includes('class="glyph"'));}
run('motion.paused=true;renderMotion()');assert.equal(node('motion-stage').dataset.paused,'true');
assert.ok(html.includes('prefers-reduced-motion:reduce'));assert.ok(html.includes('animation:none!important'));
console.log('PASS: four animation states, pause state, explicit labels and reduced-motion CSS. Temporal appearance requires interactive review.');
for(const id of ['monogram','monogram-speech','monogram-rhythm','monogram-script']){
 run(`choice.id='${id}';motion.mode='loading';motion.paused=false;renderMotion()`);
 const trace=run(`writingPaths['${id}']`);assert.equal((trace.match(/M/g)||[]).length,1,'Single continuous trace');
 assert.ok(!node('motion-stage').innerHTML.includes('class="orbit"'));
 assert.ok(node('motion-stage').innerHTML.includes(run(`concepts['${id}'].path`)),'Original geometry retained');
 assert.ok(node('motion-stage').innerHTML.includes('id="write-small"'));
 assert.ok(node('motion-stage').innerHTML.includes('id="write-large"'));
}
console.log('PASS: continuous writing masks, preserved final silhouettes and distinct mask IDs at both preview sizes.');
for(const id of ['monogram','monogram-speech','monogram-rhythm','monogram-script']){
 run(`choice.id='${id}';motion.mode='listening';renderMotion()`);
 assert.ok(!node('motion-stage').innerHTML.includes('listening-pen'));
 assert.ok(node('motion-stage').innerHTML.includes('class="listening-line"'));
 const trace=run(`listeningPath('${id}')`);assert.equal((trace.match(/M/g)||[]).length,1);
  assert.ok(trace.startsWith('M-8 '));assert.ok(!trace.includes('q'),'No added exit flourish');
 assert.ok(node('motion-stage').innerHTML.includes('id="listen-small"'));assert.ok(node('motion-stage').innerHTML.includes('id="listen-large"'));
}
assert.equal(run('listeningProgress(0).progress'),0);
assert.equal(run('listeningProgress(1040).progress'),1);
assert.equal(run('listeningProgress(1400).progress'),0);
assert.ok(run('listeningProgress(1350).ink')<1);
assert.ok(/\.listening-line\{[^}]*stroke-width:11/.test(html));
assert.ok(html.includes('brand-write 2400ms'));
console.log('PASS: pencil removed, 11-unit stroke, faster 1400ms listening cycle and unchanged loading sequence.');
