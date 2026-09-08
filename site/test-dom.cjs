// Minimal DOM for this page's text bindings and English export. Not a layout engine.
const htmlVoids=new Set(['meta','link','br','input','hr','img']);
const svgVoids=new Set(['use','path','rect','circle']);
const voids=new Set([...htmlVoids,...svgVoids]);
const escape=text=>String(text).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const decode=text=>text.replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"');
class Element {
 constructor(tag,attrs={}){this.tagName=tag.toUpperCase();this.attrs=attrs;this.childNodes=[];this.parentElement=null;this.listeners={};this.value='';this.classList={add:name=>this.setAttribute('class',[...new Set((this.attrs.class||'').split(' ').filter(Boolean).concat(name))].join(' ')),remove:name=>this.setAttribute('class',(this.attrs.class||'').split(' ').filter(x=>x!==name).join(' '))};}
 get children(){return this.childNodes.filter(x=>x instanceof Element);}
 get lang(){return this.getAttribute('lang')||'';}
 set lang(value){this.setAttribute('lang',value);}
 get textContent(){return this.childNodes.map(x=>x instanceof Element?x.textContent:x.nodeValue).join('');}
 set textContent(value){this.childNodes=[{nodeType:3,nodeValue:String(value),parentElement:this}];}
 get innerHTML(){return this.childNodes.map(serialize).join('');}
 set innerHTML(value){this.childNodes=[];parse(String(value),this);}
 getAttribute(name){return this.attrs[name]??null;}
 setAttribute(name,value){this.attrs[name]=String(value);}
 addEventListener(name,fn){this.listeners[name]=fn;}
 querySelector(selector){return all(this).find(node=>matches(node,selector))||null;}
}
function serialize(node){if(!(node instanceof Element))return escape(node.nodeValue);const tag=node.tagName.toLowerCase(),open='<'+tag+Object.entries(node.attrs).map(([k,v])=>' '+k+'="'+escape(v)+'"').join('');if(htmlVoids.has(tag))return open+'>';if(svgVoids.has(tag))return open+'/>';return open+'>'+node.innerHTML+'</'+tag+'>';}
function parse(html,parent){const stack=[parent];for(const [token]of html.matchAll(/<!--[\s\S]*?-->|<![^>]*>|<[^>]+>|[^<]+/g)){
 if(token.startsWith('<!'))continue;
 if(token.startsWith('</')){if(stack.length>1)stack.pop();continue;}
 if(token.startsWith('<')){const tag=token.match(/^<([\w-]+)/)?.[1];if(!tag)continue;const attrs={};for(const [,key,value]of token.matchAll(/([\w:-]+)="([^"]*)"/g))attrs[key]=decode(value);const node=new Element(tag,attrs);node.parentElement=stack.at(-1);node.parentElement.childNodes.push(node);if(!voids.has(tag)&&!token.endsWith('/>'))stack.push(node);}
 else stack.at(-1).childNodes.push({nodeType:3,nodeValue:decode(token),parentElement:stack.at(-1)});
}}
function all(root){return root.children.flatMap(node=>[node,...all(node)]);}
function simple(node,selector){
 if(/:(?:first-child|last-child|nth-child)/.test(selector)&&!node.parentElement)return false;
 const not=selector.match(/:not\(([^)]+)\)/);if(not){if(simple(node,not[1]))return false;selector=selector.replace(not[0],'');}
 const nth=selector.match(/:nth-child\((\d+)\)/);if(nth){if(node.parentElement.children.indexOf(node)!==Number(nth[1])-1)return false;selector=selector.replace(nth[0],'');}
 if(selector.includes(':first-child')){if(node.parentElement.children[0]!==node)return false;selector=selector.replace(':first-child','');}
 if(selector.includes(':last-child')){if(node.parentElement.children.at(-1)!==node)return false;selector=selector.replace(':last-child','');}
 const attributes=[...selector.matchAll(/\[([\w:-]+)="([^"]+)"\]/g)];for(const [,key,value]of attributes)if(node.getAttribute(key)!==value)return false;
 selector=selector.replace(/\[[^\]]+\]/g,'');const tag=selector.match(/^[\w-]+/)?.[0];if(tag&&node.tagName!==tag.toUpperCase())return false;
 const id=selector.match(/#([\w-]+)/)?.[1];if(id&&node.getAttribute('id')!==id)return false;
 for(const [,name]of selector.matchAll(/\.([\w-]+)/g))if(!(node.attrs.class||'').split(' ').includes(name))return false;
 return true;
}
function matches(node,selector){const parts=selector.split(/\s+/);function match(current,i){if(!current||!simple(current,parts[i]))return false;if(i===0)return true;if(parts[i-1]==='>')return match(current.parentElement,i-2);for(let parent=current.parentElement;parent;parent=parent.parentElement)if(match(parent,i-1))return true;return false;}return match(node,parts.length-1);}
module.exports=function makeDocument(html){const root=new Element('document');parse(html.replace(/<style>[\s\S]*?<\/style>/g,'').replace(/<script>[\s\S]*?<\/script>/g,''),root);return {hidden:false,title:'',documentElement:root.querySelector('html'),getElementById:id=>root.querySelector('#'+id),querySelector:selector=>root.querySelector(selector),serializeBody(){return root.querySelector('body').innerHTML;},addEventListener(name,fn){this[name]=fn;}};};
