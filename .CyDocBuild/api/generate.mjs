import fs from 'node:fs/promises';
import path from 'node:path';
import {importApi} from 'cydoc/api-import';

const descriptions={
cancellationToken:['Cancellation token passed to the operation.','Jeton d’annulation transmis à l’opération.'],
context:['Application, plugin package, configuration and data directories, plus the host application version.','Dossiers de l’application, du package du plugin, de configuration et de données, ainsi que la version de l’application hôte.'],
policy:['Granted project identity, root, capabilities, allowed relative roots and byte budgets.','Identité, racine, capacités, racines relatives et budgets d’octets accordés au projet.'],
files:['Project-scoped cooperative file broker supplied by the host.','Courtier de fichiers coopératif propre au projet, fourni par l’hôte.'],
projectId:['Identifier of the project registered in the host.','Identifiant du projet enregistré dans l’hôte.'],
relativeDirectory:['Directory relative to the project root; empty means the root.','Dossier relatif à la racine du projet ; une chaîne vide désigne la racine.'],
searchPattern:['File name pattern used for enumeration.','Motif de nom de fichier utilisé pour l’énumération.'],
maximumResults:['Requested result limit; the provider may apply a smaller limit.','Nombre maximal de résultats demandé ; le fournisseur peut appliquer une limite inférieure.'],
relativePath:['File path relative to an allowed project root, never an absolute path.','Chemin de fichier relatif à une racine autorisée du projet, jamais absolu.'],
content:['Bytes to write, subject to the policy write budget.','Octets à écrire, soumis au budget d’écriture de la politique.'],
settings:['Non-secret project/provider connection coordinates and token environment-variable name.','Coordonnées non secrètes du projet/fournisseur et nom de la variable d’environnement du jeton.'],
sessionToken:['Optional in-memory provider credential; do not persist it in connection settings.','Identifiant de fournisseur facultatif en mémoire ; ne pas l’enregistrer dans les réglages de connexion.'],
query:['Search text interpreted by the selected provider.','Texte de recherche interprété par le fournisseur sélectionné.'],
identifierOrUrl:['Task ID, key or supported task URL to resolve.','Identifiant, clé ou URL de tâche prise en charge à résoudre.'],
workItem:['Resolved task in the configured provider/project context.','Tâche résolue dans le contexte du fournisseur/projet configuré.'],
transition:['A transition offered by GetTransitionsAsync for the target task.','Une transition proposée par GetTransitionsAsync pour la tâche visée.'],
modeId:['Identifier of a mode declared in the provider ProjectModes collection.','Identifiant d’un mode déclaré dans la collection ProjectModes du fournisseur.'],
path:['Directory or metadata path to inspect for the selected engine.','Chemin de dossier ou de métadonnées à examiner pour le moteur sélectionné.'],
projectPath:['Root of the explicitly selected engine project.','Racine du projet moteur explicitement sélectionné.'],
cyRevisionExecutablePath:['Installed desktop executable used by the editor companion to open CyRevision.','Exécutable de bureau installé utilisé par le compagnon éditeur pour ouvrir CyRevision.'],
request:['Preview or comparison request containing the project root and the selected file paths.','Demande d’aperçu ou de comparaison contenant la racine du projet et les chemins des fichiers sélectionnés.'],
projectRoot:['Root of the selected project; never substitute an unrelated user directory.','Racine du projet sélectionné ; ne jamais la remplacer par un dossier utilisateur sans rapport.'],
filePath:['File to capture into the project store.','Fichier à capturer dans le magasin du projet.'],
isGitLfsTracked:['Whether the captured file is tracked by Git LFS.','Indique si le fichier capturé est suivi par Git LFS.'],
progress:['Optional receiver of stage, path, byte-count and item-count progress.','Récepteur facultatif de l’étape, du chemin et de l’avancement en octets/éléments.'],
versionId:['Identifier returned by ListVersionsAsync for this store.','Identifiant retourné par ListVersionsAsync pour ce magasin.']
};
const compact=value=>value.replace(/\s/g,'');
function splitParameters(text){
 const parts=[];let depth=0,start=0,quoted=false;
 for(let i=0;i<text.length;i++){const char=text[i];if(char==='"' && text[i-1]!=='\\')quoted=!quoted;if(quoted)continue;
  if('<(['.includes(char))depth++;else if('>)]'.includes(char))depth--;else if(char===',' && depth===0){parts.push(text.slice(start,i).trim());start=i+1;}}
 if(text.slice(start).trim())parts.push(text.slice(start).trim());return parts;
}
function interfaceBody(source,name){
 const start=source.indexOf('public interface '+name);if(start<0)throw Error('Missing public interface: '+name);
 const brace=source.indexOf('{',start);let depth=0;
 for(let i=brace;i<source.length;i++){if(source[i]==='{')depth++;if(source[i]==='}' && --depth===0)return source.slice(brace,i+1);}
 throw Error('Unclosed interface: '+name);
}
export async function generateApi({workspace,root,stage}){
 const rows=JSON.parse(await fs.readFile(path.join(workspace,'api/catalog.json'),'utf8'));
 const source=(await fs.readFile(path.join(root,'src/CyRevision.Plugin.Abstractions/PluginContracts.cs'),'utf8')).replace(/\/\*[\s\S]*?\*\/|\/\/[^\n]*/g,'');
 const documents={en:{symbols:[]},fr:{symbols:[]}};
 for(const row of rows){
  const[key,owner,signature,group,en,fr,returnsEn,returnsFr]=row;
  if(!compact(interfaceBody(source,owner)).includes(compact(signature)))throw Error('Public API signature changed: '+owner+' '+signature);
  const match=signature.match(/^(.+?)\s+(\w+)\(([\s\S]*)\);$/);if(!match)throw Error('Unsupported authored signature: '+key);
  for(const[locale,index]of[['en',0],['fr',1]]){
   const parameters=splitParameters(match[3]).map(parameter=>{
    const [declaration,...defaultParts]=parameter.split('='), found=declaration.trim().match(/^(.*\S)\s+(\w+)$/);
    if(!found || !descriptions[found[2]])throw Error('Parameter description missing: '+parameter);
    const detail=found[1]==='PluginProjectModeContext'
     ?['Selected project identity, root and enabled plugin IDs.','Identité, racine et identifiants des plugins activés du projet sélectionné.']
     :descriptions[found[2]];
    return{name:found[2],type:found[1],default:defaultParts.join('=').trim(),description:detail[index]};
   });
   documents[locale].symbols.push({key,name:owner+'.'+match[2],kind:'function',language:'csharp',signature:signature.slice(0,-1),description:index?fr:en,parameters,
    returnType:match[1],returns:index?returnsFr:returnsEn,references:rows.filter(other=>other[3]===group&&other[0]!==key).slice(0,3).map(other=>({key:other[0],name:other[1]+'.'+other[2].match(/\s(\w+)\(/)[1]}))});
  }
 }
 const apiDirectory=path.join(workspace,'.cache/api');await fs.mkdir(apiDirectory,{recursive:true});
 for(const locale of ['en','fr'])await fs.writeFile(path.join(apiDirectory,locale+'.json'),JSON.stringify(documents[locale]));
 const imported=await importApi(['en','fr'].map(locale=>({format:'cydoc',path:'.cache/api/'+locale+'.json',locale,prefix:'public-api'})),
  {base:workspace,locales:[{code:'en'},{code:'fr'}]});
 for(const page of imported){
  const symbol=documents[page.locale].symbols.find(symbol=>symbol.name===page.symbol),row=rows.find(row=>row[0]===symbol.key);
  const metadata={id:page.id,title:page.title,description:symbol.description,section:page.locale==='fr'?'Développeurs / API publique':'Developers / Public API',parent:row[3],kind:'api',order:200,
   ...(page.locale==='en'?{revision:1}:{sourceRevision:1})};
  const header='---\n'+Object.entries(metadata).map(([key,value])=>key+': '+value).join('\n')+'\n---\n\n';
  await fs.writeFile(path.join(stage,page.locale,page.id+'.md'),header+page.markdown);
 }
 for(const group of new Set(rows.map(row=>row[3])))for(const locale of ['en','fr']){
  const file=path.join(stage,locale,group+'.md');
  const links=imported.filter(page=>page.locale===locale && rows.find(row=>row[0]===documents[locale].symbols.find(symbol=>symbol.name===page.symbol).key)[3]===group);
  const append='\n\n## '+(locale==='fr'?'Fonctions':'Functions')+' {#functions}\n\n'+links.map(page=>'- ['+page.title+'](doc:'+page.id+')').join('\n')+'\n';
  await fs.appendFile(file,append);
 }
}
