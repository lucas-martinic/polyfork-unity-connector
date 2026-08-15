/* Runs every published asset module against the shipped JS runtime and reports what breaks.
 *
 *   node Tools~/check-modules.mjs [path/to/threejs-3d-assets/data/assets]
 *
 * The number that matters is how many of them BUILD. Local baking only helps an asset whose
 * module actually runs, and a runtime missing one common import silently sends most of the
 * catalogue to the server instead - which looks like "the editor is slow", not like a missing
 * export. Run this after touching the runtime or the module transform.
 *
 * Mirrors PolyforkModuleTransform.ToScript, so what it exercises is what the editor evaluates.
 */
import fs from 'node:fs'; import vm from 'node:vm'; import path from 'node:path';
function toScript(src){const out=['var __exports = {};'];const ex=[];
 for(let line of src.split('\n')){const tr=line.trimStart();
  if(tr.startsWith('import ')||tr.startsWith('import('))continue;
  if(tr.startsWith('export default '))line=line.replace('export default ','__exports.default = ');
  else if(tr.startsWith('export {')){const n=tr.slice(tr.indexOf('{')+1,tr.indexOf('}')).split(',').map(s=>s.trim()).filter(Boolean);
   line=n.map(x=>{const[a,,b]=x.split(/\s+/);return `__exports.${b||a} = ${a};`}).join(' ');}
  else if(tr.startsWith('export ')){const r=tr.slice(7);const m=r.match(/^(?:const|let|var|function|class)\s+([A-Za-z0-9_$]+)/);line=r;if(m)ex.push(m[1]);}
  out.push(line);} for(const n of ex)out.push(`__exports.${n} = ${n};`); return out.join('\n');}
const bundle=fs.readFileSync('/root/apps/polyfork-unity-connector/Editor/JS/three-runtime.txt','utf8');
const bridge=fs.readFileSync('/root/apps/polyfork-unity-connector/Editor/JS/polyfork-bridge.txt','utf8');
const root='/root/apps/threejs-3d-assets/data/assets';
const fails={};let ok=0,n=0;
for(const id of fs.readdirSync(root)){
  const f=path.join(root,id,'asset.public.mjs'); if(!fs.existsSync(f))continue; n++;
  const ctx={console:{log(){},warn(){},error(){}},Buffer}; vm.createContext(ctx);
  try{ vm.runInContext(bundle+'\n;globalThis.THREE=THREE;',ctx);
       // The editor evaluates a __btoa polyfill before the bridge, because QuickJS has none.
       ctx.__btoa = s => Buffer.from(Uint8Array.from(s, c => c.charCodeAt(0) & 0xff)).toString('base64');
       vm.runInContext(bridge,ctx);
       vm.runInContext(`globalThis.__polyfork.__registerSource(${JSON.stringify(id)}, ${JSON.stringify(toScript(fs.readFileSync(f,'utf8')))})`,ctx);
       vm.runInContext(`globalThis.__out = __polyfork.bake(${JSON.stringify(id)}, '{}')`,ctx);
       if(!ctx.__out) throw new Error('bake returned nothing'); ok++; }
  catch(e){ (fails[e.message] ||= []).push(id); }
}
console.log(`modules tested: ${n}, built: ${ok}, failed: ${n-ok}`);
for(const [m,ids] of Object.entries(fails).sort((a,b)=>b[1].length-a[1].length))
  console.log(`  ${ids.length.toString().padStart(3)}  ${m}   e.g. ${ids.slice(0,2).join(', ')}`);
