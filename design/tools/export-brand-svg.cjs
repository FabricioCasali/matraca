const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..');

function buildAssets(){
 const html=fs.readFileSync(path.join(root,'matraca-identidade.html'),'utf8');
 const script=html.match(/<script>([\s\S]*?)<\/script>/)[1];
 const nodes=new Map();
 const node=id=>{if(!nodes.has(id))nodes.set(id,{innerHTML:'',textContent:'',value:'',dataset:{},addEventListener(){},focus(){}});return nodes.get(id);};
 const context=vm.createContext({document:{getElementById:node,documentElement:{dataset:{},style:{setProperty(){}}},addEventListener(){},querySelector(){return node('selected');}},localStorage:{getItem(){return null;},setItem(){}},setTimeout,console});
 vm.runInContext(script,context,{timeout:1000});
 const run=code=>vm.runInContext(code,context,{timeout:1000});
 run("choice.id='monogram-speech'");
 const assets={};
 function add(name,svg){
  const prefix=name.replace(/\.svg$/,'');
  svg=svg.replace(/ aria-hidden="true"/g,'').replace(/data-listening-glyph/g,'').replace(/\s*class="glyph"/,'');
  svg=svg.replace(/(?:listen|write)-large/g,prefix+'-mask');
  if(!svg.includes('<title>'))svg=svg.replace(/(<svg\b[^>]*>)/,`$1<title>Matraca 03A</title>`);
  svg=svg.replace(/<title>[^<]*<\/title>/,'<title>Matraca 03A - m que fala</title>');
  assert.ok(!/var\(|undefined|NaN|<script|<text\b/.test(svg),'Independent SVG: '+name);
  assert.ok(svg.includes(run("concepts['monogram-speech'].path")),'Approved geometry: '+name);
  assert.ok(!/(?:href|src)="(?!#)/.test(svg),'No external resources: '+name);
  const stack=[];
  for(const tag of svg.matchAll(/<(\/?)([\w:-]+)\b[^>]*>/g)){if(tag[0].endsWith('/>'))continue;if(tag[1])assert.equal(stack.pop(),tag[2],'Balanced XML: '+name);else stack.push(tag[2]);}
  assert.equal(stack.length,0,'Closed SVG: '+name);
  if(/-(listening|loading|done)-/.test(name)){assert.ok(svg.includes('prefers-reduced-motion:reduce'));assert.ok(!svg.includes('listening-pen'));}
  assets['assets/brand/'+name]=svg+'\n';
 }
 for(const theme of ['light','dark']){
  run(`choice.theme='${theme}'`);
  add(`matraca-03a-symbol-${theme}.svg`,run("exportSvg('symbol')"));
  for(const variant of ['recording','busy','error'])add(`matraca-03a-tray-${variant}-${theme}.svg`,run(`exportSvg('${variant}')`));
  for(const [family,hue] of [['olive',125],['ochre',85],['terracotta',35],['plum',325],['teal',175]]){
   run(`choice.hue=${hue}`);add(`matraca-03a-app-${family}-${theme}.svg`,run("exportSvg('app')"));
  }
  const color=run(theme==='dark'?'rgb(.95,.003,250)':'rgb(.18,.012,250)');
  for(const state of ['listening','loading','done']){
   let svg=run(state==='listening'?"listeningSvg('monogram-speech',false)":"writingSvg('monogram-speech',false)");
   svg=svg.replace('viewBox="0 0 64 64"',state==='listening'?'viewBox="-14 -6 84 76" width="168" height="152"':'viewBox="0 0 64 64" width="128" height="128"');
   svg=svg.replaceAll('currentColor',color);
   const styles=state==='listening'?`
    .listening-trace,.listening-line{stroke-dasharray:100 100;animation:draw 1400ms linear infinite}
    .listening-line{fill:none;stroke:${color};stroke-width:11;stroke-linecap:round;stroke-linejoin:round}
    .listening-ink{animation:fade 1400ms linear infinite}
    @keyframes draw{0%{stroke-dashoffset:100}74%,100%{stroke-dashoffset:0}}
    @keyframes fade{0%,90%{opacity:1}100%{opacity:0}}
    @media(prefers-reduced-motion:reduce){*{animation:none!important}.listening-trace{stroke-dashoffset:0}.listening-line{display:none}.listening-ink{opacity:1}}
   `:state==='loading'?`
    .writing-stroke{stroke-dasharray:100 100;animation:draw 2400ms cubic-bezier(.35,0,.25,1) infinite}
    .letter-ink{animation:fade 2400ms linear infinite}
    @keyframes draw{0%,8%{stroke-dashoffset:100}68%,100%{stroke-dashoffset:0}}
    @keyframes fade{0%,84%{opacity:1}94%,100%{opacity:0}}
    @media(prefers-reduced-motion:reduce){*{animation:none!important}.writing-stroke{stroke-dashoffset:0}.letter-ink{opacity:1}}
   `:`
    .writing-stroke{stroke-dasharray:100 100;animation:draw 1100ms cubic-bezier(.35,0,.25,1) both}
    @keyframes draw{from{stroke-dashoffset:100}to{stroke-dashoffset:0}}
    @media(prefers-reduced-motion:reduce){*{animation:none!important}.writing-stroke{stroke-dashoffset:0}}
   `;
   svg=svg.replace(/(<svg\b[^>]*>)/,`$1<style>${styles}</style>`);
   add(`matraca-03a-${state}-${theme}.svg`,svg);
  }
 }
 assert.equal(Object.keys(assets).length,24);
 return assets;
}

function main(){
 const assets=buildAssets();
 const check=process.argv.includes('--check');
 for(const [name,content] of Object.entries(assets)){
  const file=path.join(root,name);
  if(check)assert.equal(fs.readFileSync(file,'utf8'),content,'Asset differs from approved source: '+name);
  else{fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,content);}
 }
 console.log((check?'PASS: ':'Exported: ')+Object.keys(assets).length+' SVG files, variant 03A only. No network or credentials.');
}
if(require.main===module)main();
module.exports={buildAssets};
