const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..');
const read=name=>fs.readFileSync(path.join(root,name),'utf8');
const tokens=JSON.parse(read('docs/design-system-tokens.json'));
const spec=read('docs/design-system.md');
const files=[
 'tools/export-brand-svg.cjs','assets/brand/README.md',
 ...Object.keys(require('../tools/export-brand-svg.cjs').buildAssets()),
 'README.md','.gitattributes',
 'docs/design-system.md','docs/design-system-tokens.json','docs/design-system-changelog.md','docs/BOARD.md',
 'docs/configuracoes-inventario.md','docs/consumo-revisao.md','docs/movimento-e-gravacao.md','docs/estudos-de-marca.md',
 'matraca-experiencia.html','matraca-identidade.html','matraca-design-system.html','matraca-ditado.html',
 'matraca-microfone.html','matraca-configuracoes.html','matraca-historico.html','matraca-gravacao.html',
 'tests/matraca-prototype.test.cjs','tests/matraca-brand.test.cjs','tests/matraca-design-system.test.cjs'
];
const manifestPath='docs/design-system-manifest.json';
assert.equal(tokens.version,'1.0.1');assert.ok(spec.includes('Versão da especificação: '+tokens.version));
assert.ok(read('matraca-design-system.html').includes(tokens.version));
const ruleIds=[...spec.matchAll(/^#{2,3} ([A-Z]+-\d{3}) /gm)].map(m=>m[1]);
assert.equal(new Set(ruleIds).size,ruleIds.length,'Unique normative rule IDs');
assert.equal(ruleIds.filter(id=>id.startsWith('CMP-')).length,10);
assert.equal(tokens.families.length,5);
const source=read('matraca-experiencia.html');
const css=source.match(/<style>([\s\S]*?)<\/style>/)[1];
const rules=[...css.matchAll(/:root([^{}]*)\{([^{}]+)\}/g)];
const normalize=value=>value.replace(/\s+/g,'').replace(/(^|[^\d])\.(\d)/g,'$10.$2');
for(const mode of ['light','dark'])for(const family of tokens.families){
 const selectors=['',`[data-palette="${family.id}"]`,'[data-palette]'];
 if(mode==='dark')selectors.push('[data-theme="dark"]','[data-palette][data-theme="dark"]');
 const actual={};
 for(const [,suffix,body] of rules)if(selectors.includes(suffix.trim()))for(const [,key,value] of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g))actual[key]=value.trim();
 assert.equal(Number(actual['--palette-hue']),family.hue);
 for(const [key,value] of Object.entries({...tokens.font,...tokens.geometry,...tokens.motion,...tokens[mode],...tokens.overlay})){
  assert.ok(actual[key],`Missing ${key}`);assert.equal(normalize(actual[key]),normalize(value),`${mode}/${family.id}/${key}`);
 }
}
const brand=read('matraca-identidade.html');
assert.ok(brand.includes(`elapsed%${tokens.brandMotion.listeningCycleMs}`));
assert.ok(brand.includes(`stroke-width:${tokens.brandMotion.listeningStrokeUnits}`));
assert.ok(brand.includes(`brand-write ${tokens.brandMotion.loadingCycleMs}ms`));
assert.ok(!brand.includes('class="listening-pen"'));
for(const file of files){
 assert.ok(fs.statSync(path.join(root,file)).isFile(),file);
 const text=read(file);
 const links=file.endsWith('.md')?[...text.matchAll(/\]\(([^)]+)\)/g)].map(m=>m[1]):file.endsWith('.html')?[...text.matchAll(/(?:src|href)="([^"]+)"/g)].map(m=>m[1]):[];
 for(const link of links){if(/^(?:#|[a-z]+:)|\$\{/.test(link))continue;const target=link.split('#')[0];if(!target)continue;const absolute=path.resolve(root,path.dirname(file),target);assert.ok(absolute.startsWith(root+path.sep),'Local reference inside package');assert.ok(fs.existsSync(absolute)||absolute===path.join(root,manifestPath),`${file} -> ${target}`);}
}
const items=files.map(file=>{const bytes=fs.readFileSync(path.join(root,file));return {path:file,bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex')};});
if(process.argv.includes('--seal')){
 const manifest={package:'matraca-design-system',specificationVersion:tokens.version,storageDecision:'project-design-directory',gitPublication:'requires-explicit-push',note:'Integrity snapshot in the Matraca repository. Git history and external backup are separate. Screenshots are excluded derivatives.',files:items};
 fs.writeFileSync(path.join(root,manifestPath),JSON.stringify(manifest,null,2)+'\n');
 console.log('Manifesto gerado para '+items.length+' arquivos-fonte. Este comando nao faz commit, push ou backup.');
}else{
 const manifest=JSON.parse(read(manifestPath));assert.equal(manifest.specificationVersion,tokens.version);assert.deepEqual(manifest.files,items,'Package changed after sealing; inspect changes before updating manifest');
 console.log('PASS: regras, tokens em dez temas, referencias locais e integridade de '+items.length+' arquivos. Nao valida conformidade visual completa ou backup.');
}
