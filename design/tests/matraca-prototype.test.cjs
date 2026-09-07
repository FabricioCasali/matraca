const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');

// A DOM stub checks transitions and generated markup, not browser layout.
const html = fs.readFileSync(path.join(__dirname, '..', 'matraca-experiencia.html'), 'utf8');
const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)];
assert.equal(scripts.length, 1);
const code = scripts[0][1];
new vm.Script(code);
assert.ok(!/scrollIntoView\s*\(/.test(code));
assert.ok(!/https?:\/\//.test(html.replace('http://www.w3.org/2000/svg', '')));
assert.ok(!/\{\{|\[REPLACE\]|lorem ipsum/i.test(html));
const timers = new Map();
const elements = new Map();
const preferences = new Map();
const events = {};
let nextTimer = 0;
const node = id => {
  if (!elements.has(id)) elements.set(id, {
    innerHTML: '', textContent: '', value: '', dataset: {}, isConnected: true,
    focus() {}, querySelector() { return node('button'); }, querySelectorAll() { return []; }
  });
  return elements.get(id);
};
const context = vm.createContext({
  console, Date, URL, setTimeout(fn) { const id = ++nextTimer; timers.set(id, fn); return id; },
  clearTimeout(id) { timers.delete(id); },
  document: { getElementById: node, documentElement: { dataset: {} }, activeElement: null,
    addEventListener(name, fn) { const previous=events[name];events[name]=event=>{previous?.(event);fn(event);}; }, querySelectorAll() { return []; } },
  window: { matchMedia() { return { matches: false, addEventListener() {} }; } },
  navigator: { platform: 'Win32', clipboard: { async writeText() {} } },
  localStorage: { getItem: key => preferences.get(key), setItem: (key, value) => preferences.set(key, value) }
});
vm.runInContext(code, context);
const run = expression => vm.runInContext(expression, context);
const press = action => run(`dispatch(${JSON.stringify(action)})`);
function drain() { let count=0; while(timers.size) { assert.ok(++count<100,'Timer loop'); const [id,fn]=timers.entries().next().value; timers.delete(id); fn(); } }
function markup() {
  const text=node('app').innerHTML;
  const ids=[...text.matchAll(/data-od-id="([^"]+)"/g)].map(m=>m[1]);
  assert.equal(new Set(ids).size, ids.length, 'Unique inspectable IDs per view');
  assert.ok(!/<[^>]*\sclass="[^"]*"[^>]*\sclass="/.test(text),'No duplicate class attribute');
  const stack=[]; const voids=new Set(['input','br','hr','img','meta','link','use']);
  for(const m of text.matchAll(/<(\/?)([a-z][\w-]*)\b[^>]*>/gi)) {
    const [,end,tag]=m; if(voids.has(tag)||m[0].endsWith('/>'))continue;
    if(end)assert.equal(stack.pop(),tag,`Balanced tag ${tag}`);else stack.push(tag);
  }
  assert.equal(stack.length,0,'Closed generated tags');
}
markup();
press('test-step'); assert.equal(run('state.step'),0,'Cannot skip permission gate');
press('prepare'); press('cancel-preparation'); drain(); assert.equal(run('state.step'),0); assert.equal(run('state.prepared'),false);
press('prepare'); drain(); assert.equal(run('state.prepared'),true); markup();
press('permissions-step'); markup(); press('test-step'); assert.equal(run('state.step'),2);
press('allow-microphone'); press('allow-writing'); press('test-step'); markup();
press('start-test'); assert.equal(run('state.status'),'listening');
press('nav-home'); assert.equal(run('state.view'),'setup','Cannot navigate during recording');
press('stop'); drain(); assert.equal(run('state.tested'),true); assert.equal(run('state.testText'),run('SAMPLE')); markup();
press('finish-setup'); assert.equal(run('state.view'),'home'); markup();
press('start-home'); press('stop'); drain(); assert.equal(run('state.history.length'),1); assert.equal(run('state.draft'),run('SAMPLE')); markup();
press('target-toggle'); press('start-home'); press('stop'); drain(); assert.equal(run('state.status'),'error'); assert.ok(run('state.pending.length>0')); markup();
press('nav-history'); press('nav-home'); assert.equal(run('state.status'),'error','Pending delivery survives navigation');
press('start-home'); assert.equal(run('state.status'),'error','New dictation cannot discard pending text');
press('retry'); drain(); assert.equal(run('state.target'),true); assert.equal(run('state.pending'),''); assert.equal(run('state.history.length'),2,'Retry does not duplicate history');
press('nav-history'); markup(); run("state.query='not-found'"); run('render()'); assert.ok(node('app').innerHTML.includes('Nenhum ditado encontrado')); press('reset-search');
run('dispatch("delete-entry-"+state.history[0].id)'); assert.equal(run('state.history.length'),1); markup();
press('clear-history'); assert.equal(run('state.modal'),'clear-history'); press('confirm-clear'); assert.equal(run('state.history.length'),0);
press('nav-settings'); markup(); press('toggle-history'); press('nav-home'); press('start-home'); press('stop'); drain(); assert.equal(run('state.history.length'),0);
const previousTheme=run('document.documentElement.dataset.theme');
press('theme'); assert.notEqual(run('document.documentElement.dataset.theme'),previousTheme);
press('nav-about'); markup();
press('preview-info'); assert.equal(run('state.modal'),'preview-info'); press('close-modal');
run("state.draft='<script>unsafe</script>'"); press('nav-home'); assert.ok(node('app').innerHTML.includes('&lt;script&gt;unsafe&lt;/script&gt;'));
press('restart-setup'); markup(); assert.equal(run('state.step'),0);
assert.ok(preferences.size>0); assert.ok(![...preferences.values()].some(x=>x.includes('unsafe')),'No text persisted');
console.log('PASS: syntax, complete markup, setup gates, cancel, dictation, recovery, history, themes, dialogs, escaping and preference-only persistence. DOM/layout not exercised.');

function luminance(L,C,h) {
  const a=C*Math.cos(h*Math.PI/180),b=C*Math.sin(h*Math.PI/180);
  const l=(L+.3963377774*a+.2158037573*b)**3;
  const m=(L-.1055613458*a-.0638541728*b)**3;
  const s=(L-.0894841775*a-1.291485548*b)**3;
  const rgb=[4.0767416621*l-3.3077115913*m+.2309699292*s,-1.2684380046*l+2.6097574011*m-.3413193965*s,-.0041960863*l-.7034186147*m+1.707614701*s].map(n=>Math.max(0,Math.min(1,n)));
  return .2126*rgb[0]+.7152*rgb[1]+.0722*rgb[2];
}
const pairs=[
 ['primary',[1,0,0],[.56,.18,255]],['primary hover',[1,0,0],[.48,.18,255]],
 ['light copy',[.43,.012,250],[.965,.004,250]],['dark copy',[.79,.008,250],[.25,.01,250]],
 ['light success',[.4,.105,155],[1,0,0]],['dark success',[.8,.1,155],[.22,.008,250]],
 ['light error',[.46,.17,25],[.97,.015,25]],['dark error',[.8,.105,25],[.26,.025,25]],
 ['HUD copy',[.79,.008,250],[.21,.013,250]],['HUD hover',[.98,.002,250],[.33,.013,250]],
 ['light tinted copy',[.43,.012,250],[.95,.026,255]],['dark tinted copy',[.79,.008,250],[.26,.045,255]],
 ['light step numbers',[.42,.12,255],[.97,.014,255]],['dark step numbers',[.83,.08,255],[.24,.025,255]],
 ['HUD wave',[.80,.10,255],[.21,.013,250]]
];
for(const [label,fg,bg] of pairs){const values=[luminance(...fg),luminance(...bg)].sort((a,b)=>b-a);const ratio=(values[0]+.05)/(values[1]+.05);assert.ok(ratio>=4.5,`${label}: ${ratio.toFixed(2)}:1`);}
assert.ok(html.includes('--action-bg: oklch(56% 0.18 255)'));
assert.ok(html.includes('background:var(--action-bg)'));
console.log(`PASS: ${pairs.length} OKLCH foreground/background pairs meet 4.5:1 (sRGB conversion).`);
for(const [id,hue] of [['olive',125],['ochre',85],['terracotta',35],['plum',325],['teal',175]]) {
  for(const theme of ['light','dark']) {
    const view=run('state.view'),step=run('state.step');
    events.change({target:{dataset:{control:'theme'},value:theme}});
    press(`color-${id}`);
    assert.equal(run('document.documentElement.dataset.palette'),id);
    assert.equal(run('document.documentElement.dataset.theme'),theme);
    assert.equal(run('state.view'),view);assert.equal(run('state.step'),step);
    assert.ok(node('app').innerHTML.includes(`value="${theme}" selected`));
    assert.ok(node('app').innerHTML.includes(`aria-pressed="true" data-action="color-${id}"`));
    assert.equal(run(`JSON.parse(localStorage.getItem(STORE)).palette`),id);
    markup();
    const ink=theme==='dark'?[.18,.012,250]:[1,0,0];
    for(const bg of theme==='dark'?[[.82,.16,hue],[.90,.12,hue]]:[[.48,.13,hue],[.40,.11,hue]]) {
      const values=[luminance(...ink),luminance(...bg)].sort((a,b)=>b-a);
      assert.ok((values[0]+.05)/(values[1]+.05)>=4.5,`${theme}-${id} button contrast`);
    }
  }
}
console.log('PASS: ten palette selections, persistence, unchanged navigation and button contrast in default/hover states.');
events.change({target:{dataset:{control:'theme'},value:'system'}});
press('color-ochre'); assert.equal(run('state.theme'),'system','Color selection preserves automatic mode');
assert.equal(run('document.documentElement.dataset.theme'),'light');
run("window.matchMedia=()=>({matches:true,addEventListener(){}}); applyTheme()");
assert.equal(run('document.documentElement.dataset.theme'),'dark');
assert.equal(run('state.palette'),'ochre');
const stored=run('localStorage.getItem(STORE)');
run("state.theme='light';state.palette='teal'");
vm.runInContext(code.match(/try \{ const p=JSON.parse\(localStorage[\s\S]*?catch\(_\) \{\}/)[0],context);
assert.equal(run('state.theme'),'system');assert.equal(run('state.palette'),'ochre');
assert.equal(run('localStorage.getItem(STORE)'),stored);
press('color-invalid'); assert.equal(run('state.palette'),'ochre');
events.change({target:{dataset:{control:'theme'},value:'invalid'}}); assert.equal(run('state.theme'),'system');
console.log('PASS: automatic mode, family preservation, saved preference restoration and invalid choice rejection.');
press('nav-microphone'); markup();
events.change({target:{dataset:{control:'mic-device'},value:'headset'}});
assert.equal(run('mic.device'),'headset');
press('mic-start'); assert.equal(run('mic.status'),'testing'); markup();
events.change({target:{dataset:{control:'mic-device'},value:'system'}});
assert.equal(run('mic.device'),'headset','No device swap mid-test');
press('color-teal'); assert.equal(run('mic.status'),'testing','Theme preserves microphone test');
events.input({target:{dataset:{control:'sensitivity'},value:'0.008'}});
assert.equal(run('deviceThreshold()'),0.008);
drain(); assert.equal(run('mic.status'),'done'); assert.equal(run('mic.detected'),true); markup();
press('mic-reset'); assert.equal(run('deviceThreshold()'),0.012);
press('mic-start'); press('mic-stop'); drain(); assert.equal(run('mic.status'),'idle');
press('mic-start'); press('nav-home'); drain(); assert.equal(run('mic.status'),'idle');
assert.ok(node('app').innerHTML.includes('Fone com microfone'),'Chosen device shown on dictation screen');
const count=run('state.history.length');
press('start-home'); press('cancel-dictation'); drain(); assert.equal(run('state.status'),'ready');assert.equal(run('state.history.length'),count);
press('nav-system'); markup(); press('ds-primary'); drain();assert.equal(run('state.history.length'),count);
for(const view of ['home','microphone','system'])for(const theme of ['light','dark'])for(const palette of ['olive','ochre','terracotta','plum','teal']){
 run(`state.view=${JSON.stringify(view)};state.theme=${JSON.stringify(theme)};state.palette=${JSON.stringify(palette)};render()`);markup();
}
console.log('PASS: microphone start/stop/finish, sensitivity, navigation cleanup, theme preservation, cancellation and markup of three views across ten themes.');
const expectedFields='modelPath language hotkey pinHotkey pinDelivery mode autoEnter beep beepVolume startSound stopSound silenceMs phraseMaxSeconds vadThreshold micSensitivity inputDevice vocabulary history historyMaxItems postProcess postProcessProvider postProcessEndpoint postProcessModel postProcessApiKey postProcessOpenAiApiKey postProcessDeepSeekApiKey postProcessReasoning postProcessPrompt postProcessTimeoutMs idleUnloadMinutes gpu focusBorder focusBorderColor focusBorderColorBusy focusBorderColorPinned focusBorderThickness focusBorderOpacity pasteMethod'.split(' ');
assert.equal(expectedFields.length,38);
assert.deepEqual(Array.from(run('CONFIG_FIELDS.map(([key])=>key)')).sort(),expectedFields.slice().sort());
const exposed=new Set();
press('nav-settings');
for(const section of ['dictation','recognition','audio','delivery','history','review','appearance']){
 press('section-'+section);
 for(const provider of ['anthropic','deepseek','openai-compatible']){
  run(`config.postProcessProvider=${JSON.stringify(provider)};render()`);markup();
  for(const match of node('app').innerHTML.matchAll(/data-(?:config-key|native-field)="([^"]+)"/g))exposed.add(match[1]);
 }
}
assert.deepEqual([...exposed].sort(),expectedFields.filter(key=>key!=='vadThreshold').sort(),'Global legacy threshold is deliberately internal; device adjustment replaces generic controls');
press('section-review');run("changeConfig('postProcessProvider','deepseek')");
assert.equal(run("changeConfig('postProcess',true)"),false);
run("changeConfig('postProcessModel','deepseek-v4-flash');changeConfig('postProcessReasoning','off')");
press('credential-example');assert.equal(run("changeConfig('postProcess',true)"),true);
press('review-example');assert.equal(run('configUI.reviewResult'),true);markup();
run("changeConfig('postProcessProvider','openai-compatible')");assert.equal(run('config.postProcess'),false);
assert.equal(run('configUI.credentials.postProcessDeepSeekApiKey'),false);
assert.equal(run("changeConfig('postProcessEndpoint','http://remote.example/api')"),false);
assert.equal(run("changeConfig('postProcessEndpoint','http://localhost:1234/v1/chat/completions')"),true);
assert.equal(run("changeConfig('postProcessEndpoint','https://example.com/v1/chat/completions')"),true);
assert.equal(run("changeConfig('postProcessEndpoint','https://user:password@example.com')"),false);
run("changeConfig('postProcessModel','modelo-configurado-pelo-usuario')");press('credential-example');
assert.equal(run("changeConfig('postProcess',true)"),false,'Invalid visible endpoint blocks activation');
run("changeConfig('postProcessEndpoint','')");assert.equal(run("changeConfig('postProcess',true)"),true);
assert.equal(run("changeConfig('silenceMs',199)"),false);assert.equal(run('config.silenceMs'),450);
assert.equal(run("changeConfig('silenceMs',500)"),true);
assert.equal(run("changeConfig('pinHotkey','F15')"),false);
assert.equal(run("changeConfig('micSensitivity','Headset = 0.012\\nheadset = 0.02')"),true);
assert.equal(run('Object.keys(config.micSensitivity).length'),1);
assert.equal(run("changeConfig('micSensitivity','Mic = 9')"),false);
assert.equal(run("changeConfig('micSensitivity','__proto__ = 0.02')"),false);
run("changeConfig('vocabulary','Matraca\\nMATRACA\\nWhisper')");assert.equal(run('config.vocabulary.length'),2);
press('section-recognition');press('download-ggml-base.bin');press('cancel-model-download');drain();assert.equal(run('config.modelPath'),'');
press('download-ggml-small.bin');drain();assert.equal(run('config.modelPath'),'models/ggml-small.bin');
run("navigator.platform='MacIntel'");press('section-audio');markup();
assert.ok(node('app').innerHTML.includes('Ainda não disponível na interface Mac'));
const beforeSound=run('config.startSound');press('config-file-startSound');assert.equal(run('config.startSound'),beforeSound);
run("navigator.platform='Win32'");press('config-file-startSound');assert.equal(run('config.startSound'),'sounds/inicio.wav');
assert.ok(![...preferences.values()].some(x=>/postProcess|micSensitivity|sounds\//.test(x)),'Config and credentials are not persisted');
for(const key of ['postProcessApiKey','postProcessOpenAiApiKey','postProcessDeepSeekApiKey'])assert.equal(run(`config.${key}`),null);
for(const theme of ['light','dark'])for(const palette of ['olive','ochre','terracotta','plum','teal'])for(const section of ['review','dictation','recognition','audio','delivery','history','appearance']){
 run(`state.theme=${JSON.stringify(theme)};state.palette=${JSON.stringify(palette)};configUI.section=${JSON.stringify(section)};render()`);markup();
}
press('nav-settings');press('section-audio');markup();
assert.ok(!node('app').innerHTML.includes('Limiar geral de voz'));
assert.ok(!node('app').innerHTML.includes('Nome do microfone ='));
assert.equal((run('micReadout()').match(/--band-height:/g)||[]).length,48);
events.change({target:{dataset:{control:'mic-device'},value:'system'}});
events.input({target:{dataset:{control:'sensitivity'},value:'0.035'}});
events.change({target:{dataset:{control:'mic-device'},value:'headset'}});
assert.equal(run('deviceThreshold()'),0.012,'New device gets own initial setting, not prior microphone value');
events.input({target:{dataset:{control:'sensitivity'},value:'0.006'}});
events.change({target:{dataset:{control:'mic-device'},value:'system'}});
assert.equal(run('deviceThreshold()'),0.035,'Switch back restores saved device setting');
press('mic-reset');assert.equal(run('deviceThreshold()'),0.012);
events.change({target:{dataset:{control:'mic-device'},value:'headset'}});assert.equal(run('deviceThreshold()'),0.006,'Reset only affects active microphone');
press('mic-start');press('section-review');drain();assert.equal(run('mic.status'),'idle');
console.log('PASS: 38-field inventory with legacy threshold internal, 48-band spectrum, per-device independence/restoration/reset, provider validation and theme coverage.');
run("state.history=[];state.keepHistory=true;state.query='';config.postProcess=false;config.historyMaxItems=100;consumption.entries=[];consumption.provider='all'");
assert.equal(run("estimateReviewCost('deepseek','deepseek-v4-flash','2026-09-06T02:00:00Z',1200,800,400,120)"),.0001728);
assert.equal(run("estimateReviewCost('deepseek','deepseek-v4-flash','2026-09-07T01:00:00Z',1200,800,400,120)"),.0003456);
assert.equal(run("estimateReviewCost('deepseek','deepseek-v4-flash','2026-09-07T04:00:00Z',1200,800,0,120)"),.0001728);
assert.equal(run("estimateReviewCost('deepseek','deepseek-v4-pro','2026-09-06T15:00:00Z',1200,800,400,120)"),.0005192);
assert.equal(run("estimateReviewCost('anthropic','unknown','2026-09-06T15:00:00Z',1200,800,400,120)"),null);
assert.equal(run("usd(null)"),'Não calculado');assert.ok(run('usd(0)').includes('0,000000'));
run("remember('Primeiro exemplo',illustrativeUsage('deepseek','deepseek-v4-flash','2026-09-06T15:00:00Z'));remember('Segundo exemplo',illustrativeUsage('deepseek','deepseek-v4-pro','2026-09-06T15:00:00Z'));remember('Outro provedor',illustrativeUsage('openai-compatible','modelo exemplo'));remember('Sem consumo informado',null)");
press('nav-history');markup();assert.equal(run('usageTotals().requests'),3);assert.equal(run('usageTotals().priced'),2);assert.equal(run('usageTotals().unknown'),1);assert.equal(run('usageTotals().tokens'),3960);
assert.ok(Math.abs(run('usageTotals().cost')-.000692)<1e-12);
assert.ok(node('app').innerHTML.includes('Subtotal estimado conhecido'));
events.change({target:{dataset:{control:'usage-provider'},value:'openai-compatible'}});markup();assert.equal(run('usageTotals().cost'),null);assert.ok(node('app').innerHTML.includes('Não calculado'));
events.change({target:{dataset:{control:'usage-provider'},value:'deepseek'}});markup();assert.equal(run('usageTotals().requests'),2);assert.equal(run('usageTotals().unknown'),0);
const reviewId=run('consumption.selectedId');press('select-history-'+reviewId);assert.equal(run('consumption.selectedId'),reviewId);
const spent=run('usageTotals().cost');press('insert-entry-'+reviewId);assert.equal(run('usageTotals().cost'),spent);
press('nav-history');press('delete-entry-'+reviewId);assert.equal(run('usageTotals().cost'),spent);
press('clear-history');press('confirm-clear');assert.equal(run('state.history.length'),0);assert.equal(run('usageTotals().cost'),spent);markup();
press('reset-history-filters');run("state.keepHistory=false;remember('Not retained',illustrativeUsage('deepseek','deepseek-v4-flash','2026-09-06T15:00:00Z'))");assert.equal(run('state.history.length'),0);assert.equal(run('usageTotals().requests'),4);
assert.ok(![...preferences.values()].some(x=>/estimatedCostUsd|promptTokens|Not retained/.test(x)));
console.log('PASS: native pricing snapshot, peak boundaries, unknown versus zero, token totals, provider filter and ledger preservation after deletion/reinsertion/history-off.');
press('nav-recording');markup();assert.ok(node('app').innerHTML.includes('recording-stage'));
assert.ok(!run("frameContent()").includes('<button'),'Native HUD stays noninteractive');
for(const status of ['ready','listening','thinking','writing','done','error']){press('frame-event-'+status);assert.equal(run('frameDemo.state'),status);markup();}
events.change({target:{dataset:{control:'frame-scenario'},value:'success'}});press('frame-play');drain();assert.equal(run('frameDemo.state'),'ready');assert.equal(node('demo-hud-slot').innerHTML,'');assert.equal(node('demo-window').dataset.active,'true','Pinned border survives idle');
press('frame-pin');assert.equal(node('demo-window').dataset.active,'false');
events.change({target:{dataset:{control:'frame-scenario'},value:'error'}});press('frame-play');drain();assert.equal(run('frameDemo.state'),'error');assert.ok(node('demo-hud-slot').innerHTML.includes('Não foi possível inserir'));
events.change({target:{dataset:{control:'frame-scenario'},value:'continuous'}});press('frame-play');drain();assert.equal(run('frameDemo.state'),'listening');
press('frame-play');press('frame-stop');drain();assert.equal(run('frameDemo.state'),'ready','Stopped sequence cannot revive HUD');
press('frame-play');press('frame-event-error');drain();assert.equal(run('frameDemo.state'),'error','Manual inspection invalidates playback');
press('frame-reduced');assert.equal(run('document.documentElement.dataset.motion'),'reduce');
events.input({target:{dataset:{control:'frame-draft'},value:'Texto em edição'}});press('color-teal');assert.equal(run('frameDemo.draft'),'Texto em edição');
press('frame-play');press('nav-home');drain();assert.equal(run('frameDemo.running'),false);
press('start-home');const unchangedHud=node('hud-root').innerHTML;run('render()');assert.equal(node('hud-root').innerHTML,unchangedHud);press('cancel-dictation');drain();
assert.ok(html.includes('prefers-reduced-motion:reduce'));assert.ok(html.includes('--motion-enter:180ms'));
console.log('PASS: six HUD states, success/error/continuous playback, cancellation, pinned border, editor preservation and reduced-motion hook. Animation timing/layout still require visual inspection.');
(async()=>{
 press('nav-recording');run("state.keepHistory=true;frameDemo.scenario='continuous';frameDemo.draft='Texto anterior.\\n';frameDemo.prompt=('Uma mensagem longa com acentuação e símbolo 🚀.\\n').repeat(60)");
 const fullPrompt=run('frameDemo.prompt');press('frame-play');drain();
 assert.equal(run('frameDemo.confirmedText'),fullPrompt,'Streaming preserves full Unicode text');
 assert.equal(run('frameDemo.draft'),'Texto anterior.\n'+fullPrompt);
 assert.equal(run('frameDemo.segments'),3);assert.equal(run('frameDemo.state'),'listening');
 assert.equal(run('state.history.find(x=>x.id===frameDemo.historyId).text'),fullPrompt);
 assert.ok(html.includes('-webkit-line-clamp:2'),'HUD has visual two-line limit');
 run("frameDemo.lastText=frameDemo.prompt;frameDemo.state='done'");run('updateFrame()');assert.ok(node('demo-hud-slot').innerHTML.includes(fullPrompt),'Only presentation truncates; text remains complete');
 run("frameDemo.prompt='Primeira frase. Segunda frase. Terceira frase.'");press('frame-play');
 for(let i=0;i<3;i++){const [id,fn]=timers.entries().next().value;timers.delete(id);fn();}
 assert.equal(run('frameDemo.segments'),1);const partial=run('frameDemo.confirmedText');press('frame-stop');drain();assert.equal(run('frameDemo.confirmedText'),partial,'Stop retains delivered chunks without appending queued text');
 press('nav-home');
 console.log('PASS: long Unicode prompt, visual-only truncation, three incremental deliveries and cancellation preserving confirmed chunks.');
 run("state.keepHistory=true;remember('Mensagem para recuperar.\\nSem perder a segunda linha.',null)");press('nav-history');markup();
 assert.ok(node('app').innerHTML.includes('Copiar mensagem'));
 const id=run('consumption.selectedId'),text=run('state.history[0].text'),cost=run('usageTotals().cost');let copied='';
 context.navigator.clipboard.writeText=async value=>{copied=value;};
 await press('copy-entry-'+id);assert.equal(copied,text);assert.ok(node('toast-root').innerHTML.includes('Texto copiado.'));
 assert.equal(run('usageTotals().cost'),cost);
 context.navigator.clipboard.writeText=async()=>{throw new Error('Blocked');};
 await press('copy-entry-'+id);assert.equal(run('state.modal'),'copy-fallback');assert.ok(node('modal-root').innerHTML.includes(text));
 console.log('PASS: copy message preserves complete text, confirms success and offers manual copy on clipboard refusal.');
})().catch(error=>{console.error(error);process.exitCode=1;});
