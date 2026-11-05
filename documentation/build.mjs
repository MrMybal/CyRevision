import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash,randomUUID} from 'node:crypto';
import {execFileSync} from 'node:child_process';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const host=path.join(root,'documentation'), target=path.join(root,'src/CyRevision.Desktop/Help');
const stage=path.join(host,'content-build'), output=path.join(root,'artifacts/cydoc-export');
const check=process.argv.includes('--check'), ensure=process.argv.includes('--ensure');
if(Number(process.versions.node.split('.')[0])<22)throw Error('CyDoc generation requires Node.js 22 or later.');
const read=async file=>(await fs.readFile(file,'utf8')).replace(/^\uFEFF/,'').replace(/\r\n?/g,'\n');
const textExtensions=new Set(['.md','.json','.mjs','.css','.html','.cs','.h','.props','.svg','.txt','.js']);
const json=async file=>JSON.parse(await read(file));
const hash=data=>createHash('sha256').update(data).digest('hex');
const posix=file=>file.split(path.sep).join('/');
async function files(directory){return(await Promise.all((await fs.readdir(directory,{withFileTypes:true})).map(async entry=>entry.isDirectory()?files(path.join(directory,entry.name)):[path.join(directory,entry.name)]))).flat().sort();}
async function removeGenerated(directory){
  const allowed=[stage,output,path.join(host,'source-api')].map(file=>path.resolve(file));
  if(!allowed.includes(path.resolve(directory)))throw Error('Refusing to remove a non-generated directory.');
  const info=await fs.lstat(directory).catch(()=>null);
  if(info?.isSymbolicLink())throw Error('Generated directory cannot be a symbolic link.');
  if(info){
    const resolved=await fs.realpath(directory), expected=path.join(await fs.realpath(root),path.relative(root,directory));
    if(resolved!==expected)throw Error('Generated directory resolves outside its expected location.');
    // Never traverse reparse points inside generated output either.
    for(const file of await files(directory))if((await fs.lstat(file)).isSymbolicLink())throw Error('Generated output contains a symbolic link.');
    await fs.rm(resolved,{recursive:true});
  }
}
async function write(file,data){await fs.mkdir(path.dirname(file),{recursive:true});await fs.writeFile(file,data);}
const lock=await json(path.join(host,'cydoc.lock.json'));
if(hash(await fs.readFile(path.join(host,lock.package)))!==lock.sha256)throw Error('CyDoc package checksum mismatch.');
const scope=await json(path.join(host,'source-scope.json'));
const references=await json(path.join(host,'references.json'));
const inputs=[...await files(path.join(host,'manual')),...await files(path.join(host,'assets')),
  ...references.references.map(entry=>path.join(root,entry.source)),...scope.map(file=>path.join(root,file)),
  ...['build.mjs','package.json','package-lock.json','cydoc.lock.json','references.json','source-scope.json','routes.json','reader-shell.html','cyrevision.css'].map(file=>path.join(host,file)),
  path.join(root,'cydoc.config.json'),path.join(root,'Directory.Build.props')];
const inputDigest=hash(Buffer.concat(await Promise.all([...new Set(inputs)].sort().map(async file=>Buffer.concat([Buffer.from(posix(path.relative(root,file))+'\0'),textExtensions.has(path.extname(file))?Buffer.from(await read(file)):await fs.readFile(file)])))));
const current=await json(path.join(target,'build-state.json')).catch(()=>null);
async function intact(){
  if(current?.inputDigest!==inputDigest)return false;
  const existing=(await files(target)).map(file=>posix(path.relative(target,file)));
  if(existing.length!==Object.keys(current.files).length+1 || existing.some(file=>file!=='build-state.json' && !(file in current.files)))return false;
  for(const[file,expected]of Object.entries(current.files))if(hash(await fs.readFile(path.join(target,file)).catch(()=>Buffer.alloc(0)))!==expected)return false;
  return true;
}
if(ensure && await intact()){console.log('CyDoc: packaged manual is current.');process.exit(0);}
const dependency=path.join(host,'node_modules/cydoc');
const npm=process.platform==='win32'?'npm.cmd':'npm';
const dependencyStamp=path.join(host,'node_modules/.cyrevision-doc-lock');
const dependencyDigest=hash(await fs.readFile(path.join(host,'package-lock.json')));
if((await read(dependencyStamp).catch(()=>''))!==dependencyDigest){
  execFileSync(npm,['ci','--no-audit','--no-fund'],{cwd:host,stdio:'inherit',shell:process.platform==='win32'});
  await fs.writeFile(dependencyStamp,dependencyDigest);
}
if(await fs.stat(path.join(host,'node_modules/three')).catch(()=>null))throw Error('The manual must not depend on Three.js.');
for(const folder of [stage,output,path.join(host,'source-api')]){await removeGenerated(folder);await fs.mkdir(folder,{recursive:true});}
await fs.cp(path.join(host,'manual'),stage,{recursive:true});
for(const file of scope)await write(path.join(host,'source-api',path.basename(file)),await read(path.join(root,file)));
const refByPath=new Map(references.references.map(entry=>[entry.source,entry]));
const coverage=[];
function slug(value){return value.normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,'')||'section';}
const front=metadata=>'---\n'+Object.entries(metadata).map(([key,value])=>`${key}: ${value}`).join('\n')+'\n---\n\n';
for(const[order,entry]of references.references.entries()){
  let body=await read(path.join(root,entry.source));
  const title=body.match(/^#\s+(.+)$/m)?.[1]||entry.id;
  const revision=hash(body).slice(0,12);
  body=body.replace(/\[([^\]]+)\]\(([^)]+)\)/g,(whole,label,href)=>{
    if(/^(?:https?:|mailto:|#|doc:)/.test(href))return whole;
    const[file,anchor]=href.split('#');
    const relative=posix(path.relative(root,path.resolve(path.dirname(path.join(root,entry.source)),file)));
    const destination=refByPath.get(relative);
    if(destination)return`[${label}](#/${destination.locale}/${destination.id}${anchor?'/'+anchor:''})`;
    return `${label} (\`${href}\`)`;
  });
  let fenced=false;const used=new Set();
  body=body.split('\n').map(line=>{
    if(line.trim().startsWith('```'))fenced=!fenced;
    if(!fenced && /^#{1,6} /.test(line) && !/\{#[\w-]+\}/.test(line)){
      const base=slug(line.replace(/^#+ /,''));let id=base,index=2;while(used.has(id))id=base+'-'+index++;used.add(id);return line+' {#'+id+'}';
    }return line;
  }).join('\n');
  const notice=entry.historical?(entry.locale==='fr'?'Référence historique : les libellés et limites peuvent décrire une ancienne version.':'Historical reference: labels and limits may describe an earlier version.'):
    (entry.locale==='fr'?'Référence technique conservée dans sa langue de maintenance. Consultez les guides actuels pour les parcours utilisateur.':'Technical reference retained in its maintained language. Use the current guides for user workflows.');
  const metadata={id:entry.id,title,description:notice,section:entry.historical?'Release history':entry.locale==='fr'?'Références techniques':'Technical references',order:120+order,revision};
  await write(path.join(stage,entry.locale,entry.id+'.md'),front(metadata)+`> ${notice}\n\n`+body);
  coverage.push({...entry,title,revision,missingLocale:entry.locale==='en'?'fr':'en'});
}
for(const locale of ['en','fr']){
  const fr=locale==='fr';
  const metadata={id:'technical-references',title:fr?'Références et couverture':'References and coverage',description:fr?'Sources conservées, langues de maintenance et limites.':'Preserved sources, maintained languages and limitations.',section:fr?'Références techniques':'Technical references',order:110,...(fr?{sourceRevision:'1'}:{revision:'1'})};
  const body=`## ${fr?'Références conservées':'Preserved references'} {#references}\n\n${fr?'Les 48 guides utilisateur sont traduits. Les références techniques ci-dessous conservent leur langue originale ; les traductions absentes ne sont pas inventées.':'The 48 user guides are translated. Technical references below retain their original language; missing translations are not fabricated.'}\n\n`+
    coverage.map(entry=>`- [${entry.title}](#/${entry.locale}/${entry.id}) — ${entry.locale.toUpperCase()} · ${fr?'traduction à compléter':'translation pending'}: ${entry.missingLocale.toUpperCase()}`).join('\n')+
    `\n\n## ${fr?'Code et limites':'Code and limitations'} {#source-limitations}\n\n${fr?'Trois fichiers publics C#/C++ sont indexés. L’index est lexical : pas d’inférence des surcharges, macros ou commentaires XML C#. Les annotations explicites produisent les pages API ; leur suivi de révision reste signalé comme non renseigné par CyDoc.':'Three public C#/C++ files are indexed. The index is lexical: it does not infer overloads, macros or C# XML comments. Explicit annotations produce API pages; their revision tracking remains reported as untracked by CyDoc.'}\n\n`+
    `## ${fr?'Historique et maintenance':'History and maintenance'} {#maintenance}\n\n${fr?'Les notes de version sont historiques. Les procédures internes de maintenance restent dans le dépôt, pas dans le manuel distribué.':'Release notes are historical. Internal maintenance procedures remain in the repository rather than the distributed manual.'}\n`;
  await write(path.join(stage,locale,'technical-references.md'),front(metadata)+body);
}
const config=await json(path.join(root,'cydoc.config.json'));
config.content='documentation/content-build';
config.version=(await read(path.join(root,'Directory.Build.props'))).match(/<VersionPrefix>([^<]+)/)[1];
const configFile=path.join(root,`.cydoc.build.${randomUUID()}.json`);
try{
  await fs.writeFile(configFile,JSON.stringify(config));
  execFileSync(process.execPath,[path.join(dependency,'tools/cli.mjs'),check?'check':'build',configFile],{cwd:root,stdio:'inherit'});
}finally{await fs.unlink(configFile).catch(()=>{});}
await fs.copyFile(path.join(host,'reader-shell.html'),path.join(output,'index.html'));
await fs.copyFile(path.join(host,'cyrevision.css'),path.join(output,'cyrevision.css'));
await fs.copyFile(path.join(dependency,'LICENSE'),path.join(output,'CyDoc-LICENSE.txt'));
await write(path.join(output,'CyDoc-SOURCE.txt'),`CyDoc ${lock.version}\n${lock.repository}\nRevision ${lock.revision}\nModule: image\n`);
await write(path.join(output,'routes.json'),JSON.stringify(await json(path.join(host,'routes.json')),null,2)+'\n');
await write(path.join(output,'coverage.json'),JSON.stringify({userGuideLanguages:['en','fr'],userGuideChapters:48,technicalReferences:coverage,sourceScope:scope,repositoryOnly:references.repositoryOnly},null,2)+'\n');
for(const file of await files(output))if(textExtensions.has(path.extname(file)))await fs.writeFile(file,await read(file));
const generated=Object.fromEntries(await Promise.all((await files(output)).map(async file=>[posix(path.relative(output,file)),hash(await fs.readFile(file))])));
await write(path.join(output,'build-state.json'),JSON.stringify({inputDigest,files:generated},null,2)+'\n');
if(check){
  for(const[file,expected]of Object.entries({...generated,'build-state.json':hash(await fs.readFile(path.join(output,'build-state.json')))}))
    if(hash(await fs.readFile(path.join(target,file)).catch(()=>Buffer.alloc(0)))!==expected)throw Error(`Packaged CyDoc output needs regeneration: ${file}`);
  const existing=(await files(target)).map(file=>posix(path.relative(target,file)));
  if(existing.length!==Object.keys(generated).length+1)throw Error('Unexpected files in the packaged CyDoc reader.');
}else{
  await fs.mkdir(target,{recursive:true});
  const desired=new Set([...Object.keys(generated),'build-state.json']);
  for(const file of await files(target))if(!desired.has(posix(path.relative(target,file))))await fs.unlink(file);
  await fs.cp(output,target,{recursive:true});
}
console.log(`CyDoc: ${check?'verified':'packaged'} local reader; image module only, ${coverage.length} preserved technical references.`);
