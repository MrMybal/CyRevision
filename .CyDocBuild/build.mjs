import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash,randomUUID} from 'node:crypto';
import {execFileSync} from 'node:child_process';

const workspace=path.dirname(fileURLToPath(import.meta.url)),root=path.dirname(workspace);
const target=path.join(root,'Documentation'),cache=path.join(workspace,'.cache');
const output=path.join(cache,'export'),stage=path.join(cache,'manual');
const check=process.argv.includes('--check'),ensure=process.argv.includes('--ensure');
if(Number(process.versions.node.split('.')[0])<22)throw Error('CyDoc generation requires Node.js 22 or later.');
const read=async file=>(await fs.readFile(file,'utf8')).replace(/^\uFEFF/,'').replace(/\r\n?/g,'\n');
const json=async file=>JSON.parse(await read(file));
const sha=data=>createHash('sha256').update(data).digest('hex');
const posix=file=>file.split(path.sep).join('/');
const textExtensions=new Set(['.md','.json','.mjs','.cs','.h','.props','.svg','.txt','.js','.css','.html']);
async function files(folder){return(await Promise.all((await fs.readdir(folder,{withFileTypes:true})).map(async entry=>entry.isDirectory()?files(path.join(folder,entry.name)):[path.join(folder,entry.name)]))).flat().sort();}
async function write(file,data){await fs.mkdir(path.dirname(file),{recursive:true});await fs.writeFile(file,data);}
async function removeGenerated(folder){
  if(![output,stage].includes(path.resolve(folder)))throw Error('Not a generated directory.');
  const info=await fs.lstat(folder).catch(()=>null);if(!info)return;
  if(info.isSymbolicLink()||await fs.realpath(folder)!==path.join(await fs.realpath(workspace),path.relative(workspace,folder)))throw Error('Unexpected generated directory location.');
  for(const file of await files(folder))if((await fs.lstat(file)).isSymbolicLink())throw Error('Generated outputs cannot contain symbolic links.');
  await fs.rm(folder,{recursive:true});
}
const lock=await json(path.join(workspace,'cydoc.lock.json'));
if(sha(await fs.readFile(path.join(workspace,lock.package)))!==lock.sha256)throw Error('CyDoc package checksum mismatch.');
const sourceScope=await json(path.join(workspace,'api/source-scope.json'));
const inputs=[...await files(path.join(workspace,'manual')),...await files(path.join(workspace,'assets')),...await files(path.join(workspace,'api')),
  ...sourceScope.map(file=>path.join(root,file)),...['build.mjs','reader.html','package.json','package-lock.json','cydoc.lock.json','cydoc.config.json','routes.json','legacy-aliases.json'].map(file=>path.join(workspace,file)),path.join(root,'Directory.Build.props')];
const inputDigest=sha(Buffer.concat(await Promise.all([...new Set(inputs)].sort().map(async file=>Buffer.concat([Buffer.from(posix(path.relative(root,file))+'\0'),textExtensions.has(path.extname(file))?Buffer.from(await read(file)):await fs.readFile(file)])))));
const stampFile=path.join(workspace,'export.lock.json'),current=await json(stampFile).catch(()=>null);
async function intact(){
  if(current?.inputDigest!==inputDigest)return false;
  const existing=await files(target).catch(()=>[]);
  if(existing.length!==Object.keys(current.files).length)return false;
  for(const[file,hash]of Object.entries(current.files))if(sha(await fs.readFile(path.join(target,file)).catch(()=>Buffer.alloc(0)))!==hash)return false;
  return true;
}
if(ensure && await intact()){console.log('CyDoc: packaged manual is current.');process.exit(0);}
const dependency=path.join(workspace,'node_modules/cydoc'),stamp=path.join(workspace,'node_modules/.cyrevision-doc-lock');
const dependencyDigest=sha(await fs.readFile(path.join(workspace,'package-lock.json')));
if((await read(stamp).catch(()=>''))!==dependencyDigest){
  execFileSync(process.platform==='win32'?'npm.cmd':'npm',['ci','--no-audit','--no-fund'],{cwd:workspace,stdio:'inherit',shell:process.platform==='win32'});
  await fs.writeFile(stamp,dependencyDigest);
}
if(await fs.stat(path.join(workspace,'node_modules/three')).catch(()=>null))throw Error('The manual does not require Three.js.');
await fs.mkdir(cache,{recursive:true});
for(const folder of [output,stage]){await removeGenerated(folder);await fs.mkdir(folder,{recursive:true});}
await fs.cp(path.join(workspace,'manual'),stage,{recursive:true});
// Normalize authored inputs before CyDoc computes implicit translation revisions.
for(const file of await files(stage))await fs.writeFile(file,await read(file));
const {generateApi}=await import('./api/generate.mjs');await generateApi({workspace,root,stage});
const config=await json(path.join(workspace,'cydoc.config.json'));
config.content='.cache/manual';config.out='.cache/export';
config.version=(await read(path.join(root,'Directory.Build.props'))).match(/<VersionPrefix>([^<]+)/)[1];
config.aliases={...await json(path.join(workspace,'legacy-aliases.json')),...config.aliases};
const configFile=path.join(workspace,'.cydoc.build.'+randomUUID()+'.json');
try{
  await fs.writeFile(configFile,JSON.stringify(config));
  execFileSync(process.execPath,[path.join(dependency,'tools/cli.mjs'),check?'check':'build',configFile],{cwd:workspace,stdio:'inherit'});
}finally{await fs.unlink(configFile).catch(()=>{});}
await fs.copyFile(path.join(workspace,'reader.html'),path.join(output,'Documentation.html'));
await fs.copyFile(path.join(dependency,'LICENSE'),path.join(output,'DocData/CyDoc-LICENSE.txt'));
await write(path.join(output,'DocData/CyDoc-SOURCE.txt'),'CyDoc '+lock.version+'\n'+lock.repository+'\nRevision '+lock.revision+'\nModules: '+lock.modules.join(', ')+'\n');
await write(path.join(output,'DocData/routes.json'),JSON.stringify(await json(path.join(workspace,'routes.json')),null,2)+'\n');
for(const file of await files(output))if(textExtensions.has(path.extname(file)))await fs.writeFile(file,await read(file));
const generated=Object.fromEntries(await Promise.all((await files(output)).map(async file=>[posix(path.relative(output,file)),sha(await fs.readFile(file))])));
const state=JSON.stringify({inputDigest,files:generated},null,2)+'\n';
if(check){
  for(const[file,hash]of Object.entries(generated))if(sha(await fs.readFile(path.join(target,file)).catch(()=>Buffer.alloc(0)))!==hash)throw Error('Regenerate the manual: '+file);
  if((await files(target)).length!==Object.keys(generated).length)throw Error('Unexpected files in Documentation.');
  if(await read(stampFile)!==state)throw Error('Regenerate the documentation lock.');
}else{
  await fs.mkdir(target,{recursive:true});
  if((await fs.lstat(target)).isSymbolicLink()||await fs.realpath(target)!==path.join(await fs.realpath(root),'Documentation'))throw Error('Unexpected manual output location.');
  const desired=new Set(Object.keys(generated));
  for(const file of await files(target))if(!desired.has(posix(path.relative(target,file))))throw Error('Unrecognized file in Documentation; preserve it before regenerating.');
  await fs.cp(output,target,{recursive:true});await fs.writeFile(stampFile,state);
}
console.log('CyDoc: '+(check?'verified':'packaged')+' bilingual manual at Documentation/Documentation.html.');
