import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {Window} from 'happy-dom';
import {createDocumentation,createLink,parseLink,hasAnchor} from 'cydoc';
import {mountViewer} from 'cydoc/viewer';
import imageModule from 'cydoc/modules/image';
import galleryModule from 'cydoc/modules/gallery';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const output=path.join(root,'Documentation'),resources=path.join(output,'DocData');
const data=JSON.parse(await fs.readFile(path.join(resources,'manifest.json'),'utf8')),docs=createDocumentation(data);

test('all human pages are completely translated with shared IDs and anchors',()=>{
  assert.equal(data.pages.length,172);assert.equal(docs.pages('en').length,86);assert.equal(data.defaultLocale,'en');
  assert.deepEqual(docs.availableLocales().map(locale=>locale.code),['en','fr']);
  for(const page of docs.pages('en')){
    const en=docs.resolve(page.id,'en'),fr=docs.resolve(page.id,'fr');assert.ok(en && fr && !fr.fallback,page.id);
    assert.notEqual(en.page.text,fr.page.text,page.id);assert.ok(en.page.description.length>10 && fr.page.description.length>10);
    assert.equal(en.page.parent,fr.page.parent);
    assert.deepEqual(en.page.headings.map(h=>h.id),fr.page.headings.map(h=>h.id),page.id);
    const media=p=>[...p.html.matchAll(/data-cydoc-id="([^"]+)"/g)].map(match=>match[1]);
    assert.deepEqual(media(en.page),media(fr.page),page.id);
  }
});

test('links, sections, media and legacy aliases all resolve',()=>{
  for(const page of data.pages)for(const href of page.links||[]){
    if(!href.startsWith('#/'))continue;
    const route=parseLink(href),resolved=docs.resolve(route.pageId,route.locale);assert.ok(resolved,href);
    if(route.anchor)assert.ok(hasAnchor(resolved.page,route.anchor),href);
    if(route.state?.media)assert.ok(resolved.page.html.includes('data-cydoc-id="'+route.state.media+'"'),href);
  }
  for(const id of Object.keys(data.aliases||{}))for(const locale of ['en','fr']){
    const route=docs.canonicalRoute({locale,pageId:id}),page=docs.resolve(route.pageId,locale)?.page;
    assert.ok(page,id);if(route.anchor)assert.ok(hasAnchor(page,route.anchor),id);
  }
  assert.equal(docs.canonicalRoute({locale:'en',pageId:'reference-server-api'}).pageId,'server-api');
  assert.equal(createLink({locale:'fr',pageId:'git-history',anchor:'file-history-including-lfs',state:{media:'history-v2'}},'file:///Example%20App/Documentation/Documentation.html'),
    'file:///Example%20App/Documentation/Documentation.html#/fr/git-history/file-history-including-lfs?media=history-v2');
});

test('distribution has the complete local reader, logo and only image/gallery modules',async()=>{
  assert.deepEqual((await fs.readdir(output)).sort(),['DocAI.md','DocData','Documentation.html']);
  for(const page of data.pages)for(const asset of page.assets||[]){
    assert.match(asset,/^assets\//);assert.ok(!asset.includes('..'));
    assert.ok((await fs.stat(path.join(resources,decodeURIComponent(asset)))).isFile());
  }
  assert.equal(data.logo,'assets/cyrevision-logo.png');assert.ok((await fs.stat(path.join(resources,data.logo))).isFile());
  assert.deepEqual((await fs.readdir(resources)).filter(name=>/^media-.*\.js$/.test(name)).sort(),['media-gallery.js','media-image.js']);
  assert.ok(!await fs.stat(path.join(root,'.CyDocBuild/node_modules/three')).catch(()=>null));
  const html=await fs.readFile(path.join(output,'Documentation.html'),'utf8');
  assert.deepEqual([...html.matchAll(/<script[^>]+src="([^"]+)"/g)].map(match=>match[1]),['DocData/data.js','DocData/viewer.js']);
  assert.match(html,/<html lang="en">/);assert.ok(!html.includes('type="module"'));assert.ok(html.includes("connect-src 'none'"));
  assert.ok((await fs.readFile(path.join(resources,'CyDoc-LICENSE.txt'),'utf8')).includes('MIT'));
});

test('reports, AI guidance and implementation listings are outside human navigation/search',async()=>{
  assert.ok(data.pages.every(page=>page.kind!=='source' && !page.id.startsWith('source-')));
  assert.ok(data.pages.every(page=>!/class GameEngineIntegrationPluginBase|SaveConnectionsCoreAsync|private readonly SemaphoreSlim/.test(page.code||'')));
  for(const id of ['technical-references','translation-report','reference-offline-guide','reference-cyui-modular-migration'])
    assert.ok(!data.pages.some(page=>page.id===id),id);
  const report=JSON.parse(await fs.readFile(path.join(resources,'translation-report.json'),'utf8'));assert.equal(report.current,86);
  for(const field of ['missing','untracked','outdated'])assert.equal(report[field].length,0);
  assert.ok((await fs.readFile(path.join(output,'DocAI.md'),'utf8')).length>100);
  const walk=async folder=>(await Promise.all((await fs.readdir(folder,{withFileTypes:true})).map(entry=>entry.isDirectory()?walk(path.join(folder,entry.name)):[entry.name]))).flat();
  assert.ok((await walk(output)).every(name=>!/\.(?:cs|cpp|h|py|tgz|map)$/i.test(name)));
});

test('public API imports contain signatures and meanings rather than source bodies',async()=>{
  const functions=data.pages.filter(page=>page.id.startsWith('public-api-'));assert.equal(functions.length,56);
  const catalog=JSON.parse(await fs.readFile(path.join(root,'.CyDocBuild/api/catalog.json'),'utf8'));
  for(const page of functions){
    assert.equal(page.kind,'api');assert.ok(page.parent);assert.ok(page.headings.some(h=>h.id==='signature'));assert.ok(page.headings.some(h=>h.id==='returns'));
    assert.ok(!page.source && !page.sourcePageId);
    assert.ok(catalog.some(row=>row[2].slice(0,-1)===page.code.trim()),page.id);
  }
  for(const locale of ['en','fr']){
    assert.ok(docs.search('OpenCode',locale,30).some(result=>result.page.id==='code-and-ai'));
    assert.ok(docs.search('ReadAllBytesAsync',locale,30).some(result=>result.page.kind==='api'));
    assert.ok(!docs.search('SourceFile',locale,30).some(result=>result.page.kind==='source'));
  }
});
function fixture(t){
  // Simulated DOM only: no navigation, script evaluation, file transport or resource fetching.
  const window=new Window({settings:{disableJavaScriptFileLoading:true,enableJavaScriptEvaluation:false,enableImageFileLoading:false,enableCSSFileLoading:false}});
  const bindings={window,document:window.document,localStorage:window.localStorage,location:window.location,navigator:window.navigator,
    ResizeObserver:window.ResizeObserver,IntersectionObserver:window.IntersectionObserver,matchMedia:window.matchMedia.bind(window),
    requestAnimationFrame:window.requestAnimationFrame.bind(window),cancelAnimationFrame:window.cancelAnimationFrame.bind(window)};
  const saved=new Map();for(const[name,value]of Object.entries(bindings)){saved.set(name,Object.getOwnPropertyDescriptor(globalThis,name));Object.defineProperty(globalThis,name,{configurable:true,writable:true,value});}
  const host=window.document.createElement('div');window.document.body.append(host);
  t.after(async()=>{await window.happyDOM.abort();window.close();for(const[name,descriptor]of saved){if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];}});
  return{window,host};
}


test('DOM fixture: hierarchy, global search, tooltips, themes and locale',t=>{
  const{window,host}=fixture(t),route={hash:'#/en/getting-started',href:'#/en/getting-started'};
  Object.defineProperty(globalThis,'location',{configurable:true,value:route});
  const viewer=mountViewer(host,data,{homePage:'getting-started',initialTheme:'dark'});
  assert.ok(host.querySelector('.topbar [data-header-search]'));assert.ok(host.querySelectorAll('.side-nav details details').length>0);
  assert.ok(!/offline|local machine|MIT license/i.test(host.querySelector('.sidebar').textContent));
  const input=host.querySelector('[data-header-search]');input.value='OpenCode';input.dispatchEvent(new window.Event('input',{bubbles:true}));
  assert.ok(host.querySelectorAll('.search-result').length>0);
  window.document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'k',ctrlKey:true,bubbles:true}));assert.ok(host.querySelector('.search-dialog').open);
  window.document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));
  const link=host.querySelector('[data-page="code-and-ai"]');link.dispatchEvent(new window.FocusEvent('focusin',{bubbles:true}));
  assert.ok(link.getAttribute('aria-describedby'));assert.ok(host.querySelector('.reader-tooltip-title').textContent.length>10);
  for(const theme of ['dark','light','contrast','nord','paper','midnight']){host.querySelector('[data-set-theme="'+theme+'"]').click();assert.equal(host.dataset.theme,theme);}
  route.hash='#/fr/getting-started';window.dispatchEvent(new window.Event('hashchange'));
  assert.match(host.querySelector('h1').textContent,/Bien démarrer/);assert.equal(host.querySelector('.locale-control select').value,'fr');
  viewer.destroy();assert.equal(host.innerHTML,'');
});

test('DOM fixture: reading and sidebar positions survive navigation',t=>{
  const{window,host}=fixture(t),route={hash:'#/en/getting-started',href:'#/en/getting-started'};
  Object.defineProperty(globalThis,'location',{configurable:true,value:route});let top=0;
  Object.defineProperty(window,'scrollY',{configurable:true,get:()=>top});window.scrollTo=options=>{top=options.top;};
  const viewer=mountViewer(host,data,{homePage:'getting-started'});
  const navigate=hash=>{route.hash=hash;window.dispatchEvent(new window.Event('hashchange'));};
  top=800;host.querySelector('.side-nav').scrollTop=160;navigate('#/en/git-history');top=420;
  navigate('#/en/getting-started');assert.equal(top,800);assert.equal(host.querySelector('.side-nav').scrollTop,160);viewer.destroy();
});

test('DOM fixture: image zoom, in-frame enlargement and Escape focus',t=>{
  const{window,host}=fixture(t),controller=new window.AbortController();
  const cleanup=imageModule.mount(host,{id:'workspace-v2',src:'assets/workspace-v2.png',alt:'Workspace',controls:'minimal'},
    {locale:'en',storageKey:'cyrevision-unit',signal:controller.signal,assetUrl:src=>'DocData/'+src});
  const expand=host.querySelector('[data-action=expand]');expand.click();
  assert.ok(host.classList.contains('media-expanded'));assert.equal(expand.getAttribute('aria-expanded'),'true');
  window.document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));
  assert.ok(!host.classList.contains('media-expanded'));assert.equal(window.document.activeElement,expand);
  host.querySelector('[data-action=in]').click();assert.equal(host.querySelector('output').textContent,'125%');
  assert.equal(host.querySelector('.media-stage').getAttribute('tabindex'),'0');assert.ok(host.classList.contains('media-minimal'));
  cleanup?.();controller.abort();
});

test('DOM fixture: gallery mounts image child with French captions and keyboard controls',async t=>{
  const{window,host}=fixture(t),controller=new window.AbortController();
  const source=await fs.readFile(path.join(root,'.CyDocBuild/manual/fr/getting-started.md'),'utf8');
  const config=JSON.parse(source.match(/[\x60]{3}gallery\s+([\s\S]*?)[\x60]{3}/)[1]);
  const cleanup=await galleryModule.mount(host,config,{locale:'fr',storageKey:'gallery-unit',signal:controller.signal,
    assetUrl:src=>'DocData/'+src,loadModule:async name=>{assert.equal(name,'image');return imageModule;}});
  await Promise.resolve();await Promise.resolve();
  assert.equal(host.querySelectorAll('[role=tab]').length,3);assert.ok(host.querySelector('.gallery-content .cy-image'));
  assert.equal(host.querySelector('.gallery-label').textContent,'Préparer les changements');
  host.querySelector('.gallery-tabs').dispatchEvent(new window.KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}));
  await Promise.resolve();await Promise.resolve();
  assert.equal(host.querySelector('[data-index="1"]').getAttribute('aria-selected'),'true');
  assert.ok(host.querySelector('[data-index="1"]').dataset.tooltipDescription);
  const expand=host.querySelector('[data-action=expand]');expand.click();assert.ok(host.classList.contains('media-expanded'));
  window.document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));
  assert.ok(!host.classList.contains('media-expanded'));cleanup?.();controller.abort();
});
