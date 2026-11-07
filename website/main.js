// Bundled as a classic script; application data and local media are embedded by build.mjs.
document.documentElement.classList.add('js');
let language='en', openedBy, current=0, savedScroll=0, savedOverflow='';
const viewer=document.querySelector('#viewer'), menu=document.querySelector('.menu');
const shotButtons=[...document.querySelectorAll('[data-shot]')];
const background=[...document.querySelectorAll('body > header, body > main, body > footer, body > .skip')];
const previousInert=new Map();
function documentationUrl(page) { return './manual/Documentation.html#/'+language+'/'+page; }
function refreshViewer() {
  const id=shotButtons[current].dataset.shot, messages=copy[language];
  const image=document.querySelector('#viewer-image');
  image.src=assetMap[id][language];image.alt=messages['shot.'+id+'.alt'];
  document.querySelector('#viewer-caption').textContent=messages['shot.'+id+'.caption'];
  document.querySelector('#viewer-count').textContent=messages['viewer.count'].replace('{current}',String(current+1)).replace('{total}',String(shotButtons.length));
}
function localize(value, updateUrl=true) {
  language=value==='fr'?'fr':'en';const messages=copy[language];
  document.documentElement.lang=language;document.title=messages['meta.title'];
  document.querySelector('meta[name="description"]').content=messages['meta.description'];
  document.querySelector('meta[property="og:title"]').content=messages['meta.title'];
  document.querySelector('meta[property="og:description"]').content=messages['meta.description'];
  for(const element of document.querySelectorAll('[data-i18n]'))element.textContent=messages[element.dataset.i18n];
  for(const element of document.querySelectorAll('[data-i18n-label]'))element.setAttribute('aria-label',messages[element.dataset.i18nLabel]);
  for(const element of document.querySelectorAll('[data-i18n-alt]'))element.alt=messages[element.dataset.i18nAlt];
  for(const image of document.querySelectorAll('[data-media]'))image.src=assetMap[image.dataset.media][language];
  for(const link of document.querySelectorAll('[data-doc]'))link.href=documentationUrl(link.dataset.doc);
  for(const button of document.querySelectorAll('[data-lang]'))button.setAttribute('aria-pressed',String(button.dataset.lang===language));
  for(const button of shotButtons)button.setAttribute('aria-label',messages['viewer.open']+': '+messages['shot.'+button.dataset.shot+'.caption']);
  if(viewer.open)refreshViewer();
  if(updateUrl){const url=new URL(location.href);url.searchParams.set('lang',language);try{history.replaceState(null,'',url);}catch{/* Some embedded file viewers disallow history changes; reading still works. */}}
}
function closeMenu(restoreFocus=false) {
  menu.setAttribute('aria-expanded','false');document.querySelector('#primary-nav').dataset.open='false';
  if(restoreFocus)menu.focus();
}
for(const button of document.querySelectorAll('[data-lang]'))button.addEventListener('click',()=>localize(button.dataset.lang));
menu.addEventListener('click',()=>{const expanded=menu.getAttribute('aria-expanded')!=='true';menu.setAttribute('aria-expanded',String(expanded));document.querySelector('#primary-nav').dataset.open=String(expanded);});
document.querySelector('#primary-nav').addEventListener('click',event=>{if(event.target.closest('a'))closeMenu();});
function openShot(button) {
  if(viewer.open)return;
  closeMenu();
  openedBy=button;current=shotButtons.indexOf(button);savedScroll=window.scrollY;savedOverflow=document.body.style.overflow;
  refreshViewer();viewer.showModal();document.body.style.overflow='hidden';
  for(const element of background){previousInert.set(element,element.inert);element.inert=true;}
  document.querySelector('[data-close]').focus();
}
for(const button of shotButtons)button.addEventListener('click',()=>openShot(button));
function moveShot(delta){current=(current+delta+shotButtons.length)%shotButtons.length;refreshViewer();}
document.querySelector('[data-prev]').addEventListener('click',()=>moveShot(-1));
document.querySelector('[data-next]').addEventListener('click',()=>moveShot(1));
document.querySelector('[data-close]').addEventListener('click',()=>viewer.close());
viewer.addEventListener('cancel',event=>{event.preventDefault();viewer.close();});
viewer.addEventListener('close',()=>{
  for(const element of background)element.inert=previousInert.get(element)??false;previousInert.clear();
  document.body.style.overflow=savedOverflow;document.querySelector('#viewer-image').removeAttribute('src');
  openedBy?.focus({preventScroll:true});window.scrollTo({top:savedScroll,left:window.scrollX,behavior:'instant'});
});
viewer.addEventListener('keydown',event=>{
  if(event.key==='Escape'){event.preventDefault();viewer.close();return;}
  if(event.key==='ArrowLeft'||event.key==='ArrowRight'){event.preventDefault();moveShot(event.key==='ArrowRight'?1:-1);}
  if(event.key==='Tab'){
    const controls=[...viewer.querySelectorAll('button:not(:disabled)')],index=controls.indexOf(document.activeElement);
    event.preventDefault();controls[(index+(event.shiftKey?-1:1)+controls.length)%controls.length].focus();
  }
});
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&!viewer.open&&menu.getAttribute('aria-expanded')==='true')closeMenu(true);});
const chapterLinks=[...document.querySelectorAll('[data-chapter]')];let queued=false;
function updateChapter(){queued=false;let active='';const threshold=document.querySelector('.site-header').offsetHeight+document.querySelector('.chapters').offsetHeight+24;
  for(const link of chapterLinks)if(document.getElementById(link.dataset.chapter).getBoundingClientRect().top<=threshold)active=link.dataset.chapter;
  for(const link of chapterLinks){if(link.dataset.chapter===active)link.setAttribute('aria-current','location');else link.removeAttribute('aria-current');}
}
function queueChapter(){if(!queued){queued=true;requestAnimationFrame(updateChapter);}}
function measureHeader(){document.documentElement.style.setProperty('--header-offset',document.querySelector('.site-header').getBoundingClientRect().height+'px');queueChapter();}
if(typeof ResizeObserver!=='undefined')new ResizeObserver(measureHeader).observe(document.querySelector('.site-header'));
window.addEventListener('scroll',queueChapter,{passive:true});window.addEventListener('resize',measureHeader);
window.addEventListener('popstate',()=>localize(new URL(location.href).searchParams.get('lang'),false));
const requested=new URL(location.href).searchParams.get('lang');localize(requested,requested!==null&&requested!=='en'&&requested!=='fr');measureHeader();
