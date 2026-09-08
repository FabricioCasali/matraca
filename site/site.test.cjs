const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const assert=require('node:assert/strict');
const html=fs.readFileSync(path.join(__dirname,'index.html'),'utf8');
const code=html.match(/<script>([\s\S]*?)<\/script>/)[1];new vm.Script(code);
const ids=[...html.matchAll(/\sid="([^"]+)"/g)].map(x=>x[1]);assert.equal(ids.length,new Set(ids).size);
for(const [,ref]of html.matchAll(/href="#([^"]+)"/g))assert.ok(ids.includes(ref),'Local anchor '+ref);
assert.equal((html.match(/class="primary"/g)||[]).length,1,'One primary download CTA');
assert.ok(html.includes('releases/download/v2.0.0/matraca-setup-2.0.0.exe'));
assert.ok(html.includes('não tem assinatura de código'));
assert.ok(html.includes('Da ideia<br><em>direto ao texto.</em>'));
assert.ok(html.includes('Seu raciocínio<br>não precisa parar.'));
assert.ok(!/dedos/i.test(html));
assert.ok(html.includes('class="voice-demo-panel"'));
assert.ok(!/class="demo-card"|class="demo-hud"/.test(html),'No fake window or overlapping HUD');
assert.ok(html.includes('01 / SUA VOZ')&&html.includes('02 / SEU TEXTO'));
assert.ok(html.includes('data-od-id="download-mac"'));
assert.ok(html.includes('O pacote macOS Apple Silicon está em preparação'));
assert.ok(!/Há pacotes públicos para Windows x64 e macOS Apple Silicon|Menos teclado\./.test(html));
assert.ok(html.includes('<link rel="canonical" href="https://fabriciocasali.github.io/matraca/">'));
assert.ok(!/getUserMedia|fetch\(|scrollIntoView|<script[^>]+src=|<link[^>]+stylesheet/.test(html));
assert.ok(!/\{\{|lorem ipsum/i.test(html));
const stack=[],voids=new Set(['meta','link','br','input','hr','img']);
for(const tag of html.matchAll(/<(\/?)([a-z][\w-]*)\b[^>]*>/g)){if(voids.has(tag[2])||tag[0].endsWith('/>'))continue;if(tag[1])assert.equal(stack.pop(),tag[2]);else stack.push(tag[2]);}assert.equal(stack.length,0);
const timers=new Map(),storage=new Map();let sequence=0;
const media={matches:false,addEventListener(name,fn){this[name]=fn;}};
const document=require('./test-dom.cjs')(html);
const el=id=>document.getElementById(id);
const context=vm.createContext({window:{matchMedia:()=>media},navigator:{language:'pt-BR'},localStorage:{getItem:key=>storage.get(key)||null,setItem:(key,value)=>storage.set(key,value)},document,setTimeout(fn){const id=++sequence;timers.set(id,fn);return id;},clearTimeout(id){timers.delete(id);}});
vm.runInContext(code,context);const run=expr=>vm.runInContext(expr,context);
function drain(){let i=0;while(timers.size){assert.ok(++i<100);const [id,fn]=timers.entries().next().value;timers.delete(id);fn();}}
assert.equal(run('playing'),false,'No autoplay');run('playDemo()');assert.equal(run('playing'),true);drain();assert.equal(el('demo-text').textContent,run('EXAMPLE'));
run('playDemo()');run('playDemo()');drain();assert.equal(run('playing'),false);
media.matches=true;run('playDemo()');assert.equal(run('playing'),false);assert.equal(el('demo-text').textContent,run('EXAMPLE'));assert.equal(timers.size,0);
media.matches=false;run('playDemo()');document.hidden=true;document.visibilitychange();drain();assert.equal(run('playing'),false);
console.log('PASS: complete HTML, local anchors, verified release URL, single primary CTA, no network code, demo playback/cancel/reduced motion/hidden page. Browser layout not exercised.');
const baseline=run('translations.map(x=>x.pt)');
document.querySelector('[data-od-id="faq-price"]').setAttribute('open','');
for(const locale of ['en','pt-BR']){
 run(`setLanguage('${locale}')`);assert.equal(document.documentElement.lang,locale);
 assert.equal(storage.get('matraca-site-language'),locale);
 assert.equal(document.title,run(`PAGE_META['${locale}'].title`));
 assert.equal(document.querySelector('[data-od-id="faq-price"]').getAttribute('open'),'','FAQ remains open');
 for(const [index,binding]of Array.from(run('translations')).entries())assert.equal(binding.type==='html'?binding.target.innerHTML:binding.target.nodeValue,locale==='en'?binding.en:baseline[index]);
 for(const binding of Array.from(run('attributes')))assert.equal(binding.target.getAttribute(binding.attribute),locale==='en'?binding.en:binding.pt);
 document.hidden=false;media.matches=false;run('playDemo()');drain();assert.equal(el('demo-text').textContent,run(`DEMO_COPY['${locale}'].example`));
 assert.equal(el('demo-button').getAttribute('aria-label'),run(`DEMO_COPY['${locale}'].playLabel`));
}
run("playDemo();setLanguage('en')");drain();assert.equal(run('playing'),false);assert.equal(el('demo-text').textContent,run('DEMO_COPY.en.initial'));
run("setLanguage('invalid')");assert.equal(run('language'),'en');
assert.ok(!document.querySelector('body').textContent.includes('O Matraca'));
assert.ok(document.querySelector('[data-od-id="faq-keys"] p').innerHTML.includes('README.md#ai-review'));
run("setLanguage('pt-BR')");assert.ok(document.querySelector('[data-od-id="faq-keys"] p').innerHTML.includes('README.pt-BR.md'));
console.log('PASS: all declared text/attribute bindings in both languages, metadata, persistence, demo localization, mid-playback switching and invalid-language fallback.');
for(const [browser,saved,expected,blocked]of [['en-US',null,'en',false],['pt-BR','en','en',false],['fr-FR',null,'pt-BR',false],['en-GB','bad','en',false],['pt-BR',null,'pt-BR',true]]){
 const doc=require('./test-dom.cjs')(html);
 const ctx=vm.createContext({document:doc,window:{matchMedia:()=>({matches:false,addEventListener(){}})},navigator:{language:browser},localStorage:{getItem(){if(blocked)throw new Error('Denied');return saved;},setItem(){if(blocked)throw new Error('Denied');}},setTimeout(){throw new Error('Unexpected autoplay');},clearTimeout(){}});
 vm.runInContext(code,ctx);assert.equal(doc.documentElement.lang,expected);
 if(blocked){vm.runInContext("setLanguage('en')",ctx);assert.equal(doc.documentElement.lang,'en');}
}
console.log('PASS: browser language, saved choice precedence, invalid saved value and blocked localStorage.');
