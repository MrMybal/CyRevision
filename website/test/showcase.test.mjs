import test,{before} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {Window} from '../../.CyDocBuild/node_modules/happy-dom/lib/index.js';
import {copy,media,destinations,packages} from '../content.mjs';
import {build,output,root,hash,files,verifyArtifact,validateTranslations} from '../build.mjs';
let html,bundle;
before(async()=>{
  await build({ensureManual:false});html=await fs.readFile(path.join(output,'index.html'),'utf8');
  const script=html.match(/<script src="\.\/([^"]+)"/)[1];bundle=await fs.readFile(path.join(output,script),'utf8');
});
async function fixture(url='file:///CyRevision%20showcase/index.html',run=true) {
  const window=new Window({url,settings:{disableJavaScriptFileLoading:true,enableJavaScriptEvaluation:true,enableImageFileLoading:false,enableCSSFileLoading:false}});
  window.fetch=()=>{throw Error('Runtime network requests are forbidden.');};
  window.document.write(html);if(run)window.eval(bundle);
  return window;
}
async function finish(window){await window.happyDOM.abort();window.close();}
const select=(window,selector)=>window.document.querySelector(selector);
test('complete translations, no placeholders, product identity and verified release filenames',()=>{
  validateTranslations();const broken=structuredClone(copy);delete broken.fr['hero.body'];assert.throws(()=>validateTranslations(broken));
  broken.fr['hero.body']='';assert.throws(()=>validateTranslations(broken));
  assert.ok(Object.keys(copy.en).length>100);
  assert.match(destinations.release,/CyRevision\/releases\/tag\/v0\.1\.34$/);
  assert.equal(packages.length,5);assert.equal(new Set(packages.flatMap(p=>p.slice(1))).size,10);
  assert.ok(packages.every(p=>p.slice(1).every(name=>name.includes('0.1.34'))));
  for(const locale of ['en','fr'])for(const [key,value]of Object.entries(copy[locale]))assert.ok(!/TODO|PLACEHOLDER|APP_NAME|lorem ipsum/i.test(value),key);
});
test('English prerender, landmarks, complete captures and local resources without JavaScript',async()=>{
  const w=await fixture(undefined,false);
  try {
    assert.equal(w.document.documentElement.lang,'en');assert.equal(select(w,'h1').textContent,'CyRevision');
    assert.equal(w.document.querySelectorAll('[data-shot]').length,8);
    assert.equal(w.document.querySelectorAll('main>section').length,10);
    assert.ok(select(w,'.skip').getAttribute('href')==='#main');
    const hero=select(w,'.hero img[data-media]');assert.equal(hero.width,1484);assert.equal(hero.height,921);assert.equal(hero.loading,'eager');
    assert.equal(select(w,'.header-actions .github').textContent,'GitHub ↗');
    for(const image of w.document.querySelectorAll('img[data-media]'))assert.ok(image.alt.length>15);
    for(const element of w.document.querySelectorAll('script[src],link[rel=stylesheet],img[src]'))assert.ok(element.getAttribute('src')?.startsWith('./assets/')||element.getAttribute('href')?.startsWith('./assets/'));
  }finally{await finish(w);}
});
test('French deep URL, document metadata, all visible strings, media and contextual reader destinations',async()=>{
  const w=await fixture('file:///CyRevision%20showcase/index.html?keep=yes&lang=fr#history');
  try {
    assert.equal(w.document.documentElement.lang,'fr');assert.equal(w.document.title,copy.fr['meta.title']);
    for(const element of w.document.querySelectorAll('[data-i18n]'))assert.equal(element.textContent,copy.fr[element.dataset.i18n]);
    for(const element of w.document.querySelectorAll('[data-i18n-label]'))assert.equal(element.getAttribute('aria-label'),copy.fr[element.dataset.i18nLabel]);
    for(const element of w.document.querySelectorAll('[data-i18n-alt]'))assert.equal(element.alt,copy.fr[element.dataset.i18nAlt]);
    assert.ok(select(w,'img[data-media=modes]').getAttribute('src').includes('modes.fr.'));
    for(const link of w.document.querySelectorAll('[data-doc]'))assert.ok(link.href.includes('manual/Documentation.html#/fr/'+link.dataset.doc));
    select(w,'[data-lang=en]').click();assert.equal(w.document.documentElement.lang,'en');
    assert.equal(new URL(w.location.href).searchParams.get('keep'),'yes');assert.equal(w.location.hash,'#history');
    assert.equal(select(w,'[data-lang=en]').getAttribute('aria-pressed'),'true');assert.equal(select(w,'[data-lang=fr]').getAttribute('aria-pressed'),'false');
  }finally{await finish(w);}
});
test('unsupported locales normalize to English; static-subpath and moved-file links remain relative',async()=>{
  for(const url of ['file:///Moved%20folder/%C3%A9/index.html?lang=es&keep=1#manual','https://example.invalid/nested/project/index.html?lang=es&keep=1#manual']){
    const w=await fixture(url);try{
      assert.equal(w.document.documentElement.lang,'en');assert.equal(new URL(w.location.href).searchParams.get('lang'),'en');
      assert.equal(w.location.hash,'#manual');assert.equal(new URL(w.location.href).searchParams.get('keep'),'1');
      assert.equal(select(w,'[data-doc]').href,new URL('./manual/Documentation.html#/en/getting-started',url).href);
    }finally{await finish(w);}
  }
});
test('mobile menu exposes state, closes on navigation and restores focus with Escape',async()=>{
  const w=await fixture();try{
    const menu=select(w,'.menu'),nav=select(w,'#primary-nav');menu.click();assert.equal(menu.getAttribute('aria-expanded'),'true');assert.equal(nav.dataset.open,'true');
    nav.querySelector('a').click();assert.equal(menu.getAttribute('aria-expanded'),'false');
    menu.click();w.document.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));
    assert.equal(menu.getAttribute('aria-expanded'),'false');assert.equal(w.document.activeElement,menu);
  }finally{await finish(w);}
});
test('image viewer opens from keyboard-equivalent button activation, navigates, traps focus, closes and restores scroll',async()=>{
  const w=await fixture();try{
    const calls=[];w.scrollTo=value=>calls.push(value);Object.defineProperty(w,'scrollY',{configurable:true,value:420});
    const button=select(w,'[data-shot=history]'),dialog=select(w,'#viewer'),close=select(w,'[data-close]');
    button.focus();button.click();assert.equal(dialog.open,true);assert.equal(w.document.activeElement,close);assert.equal(select(w,'main').inert,true);assert.equal(w.document.body.style.overflow,'hidden');
    assert.equal(select(w,'#viewer-image').alt,copy.en['shot.history.alt']);
    dialog.dispatchEvent(new w.KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}));assert.equal(select(w,'#viewer-image').alt,copy.en['shot.diff.alt']);
    select(w,'[data-prev]').click();assert.equal(select(w,'#viewer-image').alt,copy.en['shot.history.alt']);
    close.focus();dialog.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Tab',shiftKey:true,bubbles:true}));assert.equal(w.document.activeElement,select(w,'[data-next]'));
    dialog.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));
    assert.equal(dialog.open,false);assert.equal(select(w,'main').inert,false);assert.equal(w.document.body.style.overflow,'');assert.equal(w.document.activeElement,button);assert.equal(calls.at(-1).top,420);
    button.click();close.click();assert.equal(dialog.open,false);
  }finally{await finish(w);}
});
test('French viewer captions/counts and all eight image sources follow the selected locale',async()=>{
  const w=await fixture('file:///site/index.html?lang=fr');try{
    select(w,'[data-shot=modes]').click();assert.equal(select(w,'#viewer-caption').textContent,copy.fr['shot.modes.caption']);
    assert.match(select(w,'#viewer-count').textContent,/Image 2 sur 8/);assert.ok(select(w,'#viewer-image').getAttribute('src').includes('modes.fr.'));
    for(let n=0;n<8;n++)select(w,'[data-next]').click();assert.equal(select(w,'#viewer-caption').textContent,copy.fr['shot.modes.caption']);
    select(w,'#viewer').dispatchEvent(new w.Event('cancel',{cancelable:true}));assert.equal(select(w,'#viewer').open,false);
  }finally{await finish(w);}
});
test('artifact boundaries, genuine unchanged manual, two locales and local classic-script bootstrap',async()=>{
  const checked=await verifyArtifact();assert.equal(checked.pages,172);
  const lock=JSON.parse(await fs.readFile(path.join(root,'.CyDocBuild/export.lock.json'),'utf8'));
  for(const [name,digest]of Object.entries(lock.files))assert.equal(hash(await fs.readFile(path.join(output,'manual',name))),digest,name);
  const names=(await files(output)).map(file=>path.relative(output,file).replaceAll('\\','/'));
  assert.ok(names.every(name=>!/(?:^|\/)(?:\.git|\.cyrevision|\.CyDocBuild|node_modules|profiles|databases)(?:\/|$)/.test(name)));
  assert.ok(names.every(name=>! /\.(?:exe|dll|pfx|p12|db|log|pem|key)$/i.test(name)));
  const reader=await fs.readFile(path.join(output,'manual/Documentation.html'),'utf8');assert.match(reader,/DocData\/data.js/);assert.match(reader,/DocData\/viewer.js/);assert.doesNotMatch(reader,/type="module"|fetch\(/);
  assert.doesNotMatch(bundle,/\bfetch\s*\(|XMLHttpRequest|WebSocket|requestFullscreen/);
});
test('stable builds, hash renewal when content changes, no runtime CSS dependency',async()=>{
  const before=await fs.readFile(path.join(output,'site-output.json'),'utf8');await build({ensureManual:false});
  assert.equal(await fs.readFile(path.join(output,'site-output.json'),'utf8'),before);
  for(const file of await files(path.join(output,'assets'))){const digest=path.basename(file).match(/\.([a-f0-9]{12})\./)?.[1];assert.equal(hash(await fs.readFile(file)).slice(0,12),digest);}
  for(const name of ['styles.css','main.js']){const data=await fs.readFile(path.join(root,'website',name),'utf8');assert.notEqual(hash(data),hash(data+'\n/* changed */'));}
  const css=await fs.readFile(path.join(root,'website/styles.css'),'utf8');assert.doesNotMatch(css,/@import|https?:\/\/|object-fit:cover/);assert.match(css,/object-fit:contain/);assert.match(css,/prefers-reduced-motion/);assert.match(css,/@media\(max-width:760px\)/);
});
test('reject unrecognized output files rather than delete user additions',async()=>{
  const sentinel=path.join(output,'user-added-test.txt');
  await fs.writeFile(sentinel,'Disposable test fixture.');
  try{await assert.rejects(build({ensureManual:false}),/Unexpected or missing artifact files/);assert.equal(await fs.readFile(sentinel,'utf8'),'Disposable test fixture.');}
  finally{await fs.unlink(sentinel);}
  await verifyArtifact();
});
