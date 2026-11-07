import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash, randomUUID} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {Script} from 'node:vm';
import {copy, media, destinations, packages} from './content.mjs';
import {renderPage} from './template.mjs';
export const source=path.dirname(fileURLToPath(import.meta.url)),root=path.dirname(source),output=path.join(root,'dist/website');
export const hash=data=>createHash('sha256').update(data).digest('hex');
const readText=async file=>(await fs.readFile(file,'utf8')).replace(/\r\n?/g,'\n');
export async function files(directory) {
  const result=[];
  for(const entry of await fs.readdir(directory,{withFileTypes:true})) {
    const file=path.join(directory,entry.name);
    if(entry.isSymbolicLink())throw Error('Symbolic links are not accepted in showcase inputs/output.');
    if(entry.isDirectory())result.push(...await files(file));else if(entry.isFile())result.push(file);else throw Error('Unsupported file type.');
  }
  return result.sort();
}
export function validateTranslations(messages=copy) {
  if(JSON.stringify(Object.keys(messages.en).sort())!==JSON.stringify(Object.keys(messages.fr).sort()))throw Error('Translation keys differ.');
  for(const [locale,strings]of Object.entries(messages))for(const [key,value]of Object.entries(strings))if(typeof value!=='string'||!value.trim())throw Error('Empty translation: '+locale+'/'+key);
}
export async function verifyArtifact(directory=output) {
  const record=JSON.parse(await fs.readFile(path.join(directory,'site-output.json'),'utf8'));
  if(record.format!=='cyrevision-showcase-1')throw Error('Not a showcase output.');
  const actual=(await files(directory)).map(file=>path.relative(directory,file).split(path.sep).join('/')).filter(file=>file!=='site-output.json');
  if(JSON.stringify(actual.sort())!==JSON.stringify(Object.keys(record.files).sort()))throw Error('Unexpected or missing artifact files.');
  for(const [name,digest]of Object.entries(record.files))if(hash(await fs.readFile(path.join(directory,name)))!==digest)throw Error('Artifact checksum mismatch: '+name);
  const html=await fs.readFile(path.join(directory,'index.html'),'utf8');
  const manifest=JSON.parse(await fs.readFile(path.join(directory,'manual/DocData/manifest.json'),'utf8'));
  const ids=new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(match=>match[1]));
  for(const match of html.matchAll(/(?:href|src)="([^"]+)"/g)) {
    const value=match[1];if(/^https:\/\//.test(value))continue;
    if(/^(?:[a-z][\w+.-]*:|\/\/|\/)/i.test(value)||value==='#'||!value)throw Error('Nonportable/empty link: '+value);
    if(value.startsWith('#')){if(!ids.has(value.slice(1)))throw Error('Missing chapter anchor: '+value);continue;}
    const [name,fragment]=value.split('#',2),file=path.resolve(directory,decodeURIComponent(name.split('?')[0]));
    if(!file.startsWith(path.resolve(directory)+path.sep))throw Error('Reference escapes output.');
    if(!(await fs.stat(file)).isFile())throw Error('Missing local file: '+value);
    if(name.endsWith('Documentation.html')&&fragment){const[,locale,id]=fragment.split('/');if(!manifest.pages.some(page=>page.id===id&&page.locale===locale))throw Error('Invalid reader route: '+value);}
  }
  for(const target of new Set([...html.matchAll(/data-doc="([^"]+)"/g)].map(match=>match[1])))for(const locale of ['en','fr'])if(!manifest.pages.some(page=>page.id===target&&page.locale===locale))throw Error('Missing contextual manual translation: '+locale+'/'+target);
  for(const match of html.matchAll(/data-i18n(?:-label|-alt)?="([^"]+)"/g))if(!copy.en[match[1]]||!copy.fr[match[1]])throw Error('Missing page string.');
  if(/__CSS__|__JS__|href="#"/.test(html))throw Error('Unresolved placeholder.');
  return {files:actual.length,pages:manifest.pages.length};
}
async function checkReplaceable(directory) {
  if(!(await fs.stat(directory).catch(()=>null)))return;
  if((await fs.lstat(directory)).isSymbolicLink()||await fs.realpath(directory)!==path.join(await fs.realpath(path.dirname(directory)),path.basename(directory)))throw Error('Unexpected output location.');
  // Refuse to destroy anything the previous build did not generate or whose bytes were edited.
  await verifyArtifact(directory);
}
export async function build({ensureManual=true}={}) {
  if(Number(process.versions.node.split('.')[0])<22)throw Error('Node.js 22+ is required.');
  validateTranslations();
  if(ensureManual)execFileSync(process.execPath,[path.join(root,'.CyDocBuild/build.mjs'),'--ensure'],{cwd:root,stdio:'inherit'});
  const lock=JSON.parse(await fs.readFile(path.join(root,'.CyDocBuild/export.lock.json'),'utf8'));
  const manual=path.join(root,'Documentation'),manualFiles=await files(manual);
  if(manualFiles.length!==Object.keys(lock.files).length)throw Error('Manual contains unrecognized files. Regenerate it first.');
  for(const [name,digest]of Object.entries(lock.files))if(hash(await fs.readFile(path.join(manual,name)))!==digest)throw Error('Manual export differs from its lock.');
  await fs.mkdir(path.dirname(output),{recursive:true});
  if((await fs.lstat(path.dirname(output))).isSymbolicLink()||await fs.realpath(path.dirname(output))!==path.join(await fs.realpath(root),'dist'))throw Error('Unexpected dist directory location.');
  await checkReplaceable(output);
  const stage=path.join(path.dirname(output),'.website-stage-'+randomUUID());await fs.mkdir(stage);
  const put=async(name,data)=>{const file=path.join(stage,name);await fs.mkdir(path.dirname(file),{recursive:true});await fs.writeFile(file,data);};
  try {
    const assets={};
    async function asset(name,filename){const data=await fs.readFile(path.join(root,filename));const destination='assets/'+name+'.'+hash(data).slice(0,12)+path.extname(filename);await put(destination,data);return './'+destination;}
    assets.logo=await asset('cyrevision','src/CyRevision.Desktop/Assets/Branding/cyrevision-cy-simple-badge-transparent.png');
    for(const item of media){const en=await asset(item.id,item.source);assets[item.id]={en,fr:item.french?await asset(item.id+'.fr',item.french):en};}
    const css=await readText(path.join(source,'styles.css'));
    const js='// CyRevision showcase. Application license: AGPL-3.0.\n(()=>{\nconst copy='+JSON.stringify(copy).replaceAll('<','\\u003c')+';\nconst assetMap='+JSON.stringify(assets)+';\n'+await readText(path.join(source,'main.js'))+'\n})();\n';
    new Script(js,{filename:'showcase.js'});
    const cssName='assets/site.'+hash(css).slice(0,12)+'.css',jsName='assets/site.'+hash(js).slice(0,12)+'.js';
    await put(cssName,css);await put(jsName,js);
    await put('index.html',renderPage(assets).replace('__CSS__','./'+cssName).replace('__JS__','./'+jsName));
    await put('LICENSE',await fs.readFile(path.join(root,'LICENSE')));
    await put('NOTICES.txt','CyRevision showcase and application: AGPL-3.0; see LICENSE.\nSelected screenshots, branding and diagrams originate in this application repository.\nThe complete unmodified local manual is in manual/.\nCyDoc reader: MIT; see manual/DocData/CyDoc-LICENSE.txt and CyDoc-SOURCE.txt.\nNo third-party website framework, runtime fonts or browser engine is bundled by this showcase.\n');
    for(const file of manualFiles)await put('manual/'+path.relative(manual,file),await fs.readFile(file));
    const content=Object.fromEntries(await Promise.all((await files(stage)).map(async file=>[path.relative(stage,file).split(path.sep).join('/'),hash(await fs.readFile(file))])));
    await put('site-output.json',JSON.stringify({format:'cyrevision-showcase-1',files:content},null,2)+'\n');
    await verifyArtifact(stage);
    // Exact app-owned generated destination, verified above; no repository or arbitrary directory is removed.
    if(await fs.stat(output).catch(()=>null))await fs.rm(output,{recursive:true});
    await fs.rename(stage,output);
    return output;
  } catch(error) {
    // The unpredictable stage belongs exclusively to this invocation, never to a caller-supplied path.
    await fs.rm(stage,{recursive:true,force:true});throw error;
  }
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  if(process.argv.includes('--check'))console.log('Showcase verified:',await verifyArtifact());
  else console.log('Showcase built:',await build());
}
