import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {Window} from 'happy-dom';
import {createDocumentation,createLink,parseLink,hasAnchor} from 'cydoc';
import {mountViewer} from 'cydoc/viewer';
import imageModule from 'cydoc/modules/image';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const help=path.join(root,'src/CyRevision.Desktop/Help');
const data=JSON.parse(await fs.readFile(path.join(help,'manifest.json'),'utf8'));
const docs=createDocumentation(data);
const ids=(await fs.readdir(path.join(root,'documentation/manual/en'))).filter(file=>file.endsWith('.md')).map(file=>file.slice(0,-3));

test('the 48 translated guides retain their IDs, anchors and media identifiers',()=>{
  assert.equal(ids.length,48);assert.equal(data.defaultLocale,'en');
  for(const id of ids){
    const en=docs.resolve(id,'en'),fr=docs.resolve(id,'fr');
    assert.ok(en && fr && !fr.fallback,id);
    assert.deepEqual(en.page.headings.map(h=>h.id),fr.page.headings.map(h=>h.id),id);
    const media=page=>[...page.html.matchAll(/data-cydoc-id="([^"]+)"/g)].map(match=>match[1]);
    assert.deepEqual(media(en.page),media(fr.page),id);
    assert.ok(media(en.page).length>0,id);
  }
});

test('all compiled internal page, anchor and media links resolve',()=>{
  for(const page of data.pages)for(const href of page.links||[]){
    if(!href.startsWith('#/'))continue;
    const route=parseLink(href);const resolved=docs.resolve(route.pageId,route.locale);
    assert.ok(resolved,href);
    if(route.anchor)assert.ok(hasAnchor(resolved.page,route.anchor),href);
    if(route.state?.media)assert.ok(resolved.page.html.includes('data-cydoc-id="'+route.state.media+'"'),href);
  }
  assert.equal(docs.canonicalRoute({locale:'en',pageId:'quickstart'}).pageId,'getting-started');
  const link=createLink({locale:'fr',pageId:'git-history',anchor:'file-history-including-lfs',state:{media:'history-v2'}},'file:///Example%20App/Help/index.html');
  assert.equal(link,'file:///Example%20App/Help/index.html#/fr/git-history/file-history-including-lfs?media=history-v2');
});

test('all local assets exist and only image runtime is distributed',async()=>{
  for(const page of data.pages)for(const asset of page.assets||[]){
    assert.match(asset,/^assets\//);assert.ok(!asset.includes('..'));
    assert.ok((await fs.stat(path.join(help,decodeURIComponent(asset)))).isFile());
  }
  const names=await fs.readdir(help);
  assert.deepEqual(names.filter(name=>/^media-.*\.js$/.test(name)),['media-image.js']);
  assert.ok(!await fs.stat(path.join(root,'documentation/node_modules/three')).catch(()=>null));
  const html=await fs.readFile(path.join(help,'index.html'),'utf8');
  assert.deepEqual([...html.matchAll(/<script[^>]+src="([^"]+)"/g)].map(match=>match[1]),['data.js','viewer.js']);
  assert.ok(!html.includes('type="module"'));assert.ok(html.includes("connect-src 'none'"));
  assert.ok((await fs.readFile(path.join(help,'CyDoc-LICENSE.txt'),'utf8')).includes('MIT'));
});

test('source scope and preexisting translation gaps are explicit',async()=>{
  const coverage=JSON.parse(await fs.readFile(path.join(help,'coverage.json'),'utf8'));
  assert.equal(coverage.sourceScope.length,3);assert.equal(coverage.technicalReferences.length,38);
  assert.equal(data.pages.filter(page=>page.kind==='source').length,6);
  assert.equal(coverage.technicalReferences.filter(page=>page.missingLocale==='en').length,12);
  const report=JSON.parse(await fs.readFile(path.join(help,'translation-report.json'),'utf8'));
  assert.equal(report.missing.length,26);assert.equal(report.untracked.length,3);assert.equal(report.outdated.length,0);
  assert.ok(data.pages.every(page=>!page.source || !/^[A-Z]:|Users\//i.test(page.source)));
});

test('search finds OpenCode, build logs and source contracts in both languages',()=>{
  for(const locale of ['en','fr']){
    assert.ok(docs.search('OpenCode',locale,30).some(result=>result.page.id==='code-and-ai'));
    assert.ok(docs.search('IPluginProjectFileSandbox',locale,30).length>0);
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

test('DOM fixture: CyDoc navigation restores reading positions and explicit sections',t=>{
  const{window,host}=fixture(t);const route={hash:'#/en/getting-started',href:'#/en/getting-started'};
  Object.defineProperty(globalThis,'location',{configurable:true,value:route});let top=0;
  Object.defineProperty(window,'scrollY',{configurable:true,get:()=>top});window.scrollTo=options=>{top=options.top;};
  const viewer=mountViewer(host,data,{initialTheme:'dark',homePage:'getting-started'});
  const navigate=hash=>{route.hash=hash;window.dispatchEvent(new window.Event('hashchange'));};
  assert.match(host.querySelector('h1').textContent,/Getting started/);
  top=800;navigate('#/en/git-history');assert.equal(top,0);top=420;
  navigate('#/en/getting-started');assert.equal(top,800);
  navigate('#/fr/getting-started');assert.match(host.querySelector('h1').textContent,/Bien démarrer/);
  for(const theme of ['dark','light','contrast']){host.querySelector('[data-set-theme='+theme+']').click();assert.equal(host.dataset.theme,theme);}
  viewer.destroy();assert.equal(host.innerHTML,'');
});

test('DOM fixture: image enlargement stays in-frame and Escape restores it',t=>{
  const{window,host}=fixture(t);const controller=new window.AbortController();
  const cleanup=imageModule.mount(host,{id:'workspace-v2',src:'assets/workspace-v2.png',alt:'Workspace',controls:'minimal'},
    {locale:'en',storageKey:'cyrevision-unit',signal:controller.signal});
  const expand=host.querySelector('[data-action=expand]');expand.click();
  assert.ok(host.classList.contains('media-expanded'));assert.equal(expand.getAttribute('aria-expanded'),'true');
  window.document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));
  assert.ok(!host.classList.contains('media-expanded'));assert.equal(window.document.activeElement,expand);
  host.querySelector('[data-action=in]').click();assert.equal(host.querySelector('output').textContent,'125%');
  assert.equal(host.querySelector('.media-stage').getAttribute('tabindex'),'0');
  assert.ok(host.classList.contains('media-minimal'));
  cleanup?.();controller.abort();
});
