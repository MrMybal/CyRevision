import {copy, media, destinations, packages} from './content.mjs';
export const escape = value=>String(value).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
export function renderPage(assets) {
  const t=key=>escape(copy.en[key]??(()=>{throw Error('Missing copy: '+key);})());
  const text=(tag,key,attrs='')=>`<${tag} data-i18n="${key}" ${attrs}>${t(key)}</${tag}>`;
  const doc=(page,key,cls='text-link')=>`<a class="${cls}" data-doc="${page}" href="./manual/Documentation.html#/en/${page}">${t(key)} ↗</a>`;
  const shot=(id,priority=false)=>{
    const item=media.find(item=>item.id===id);
    return `<figure class="screen"><button type="button" class="shot" data-shot="${id}" aria-label="${t('viewer.open')}: ${t('shot.'+id+'.caption')}"><img data-media="${id}" src="${assets[id].en}" data-i18n-alt="shot.${id}.alt" alt="${t('shot.'+id+'.alt')}" width="${item.width}" height="${item.height}" ${priority?'loading="eager" fetchpriority="high"':'loading="lazy"'} decoding="async"></button>${text('figcaption','shot.'+id+'.caption')}</figure>`;
  };
  const intro=(id)=>text('p',id+'.kicker','class="eyebrow"')+text('h2',id+'.title',`id="${id}-title"`)+text('p',id+'.body');
  const chapters=['modes','history','assets','review','recovery','tools','manual','download'];
  const list=(id)=>`<ul>${[1,2,3].map(n=>text('li',id+'.item'+n)).join('')}</ul>`;
  return `<!doctype html>
<html lang="en"><head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark"><meta name="theme-color" content="#131619">
<meta http-equiv="Content-Security-Policy" content="default-src 'self' file: data:; script-src 'self' file:; style-src 'self' file:; img-src 'self' file: data:; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'">
<title>${t('meta.title')}</title><meta name="description" content="${t('meta.description')}">
<meta property="og:type" content="website"><meta property="og:title" content="${t('meta.title')}"><meta property="og:description" content="${t('meta.description')}"><meta property="og:image" content="${assets.workspace.en}">
<link rel="icon" href="${assets.logo}"><link rel="stylesheet" href="__CSS__"><script src="__JS__" defer></script>
</head><body>
<a class="skip" href="#main" data-i18n="skip">${t('skip')}</a>
<header class="site-header"><div class="wrap header-inner">
<a class="brand" href="#main"><img src="${assets.logo}" width="32" height="32" alt=""><span>CyRevision</span></a>
<nav id="primary-nav" class="primary-nav" data-i18n-label="nav.label" aria-label="${t('nav.label')}"><a href="#modes" data-i18n="nav.features">${t('nav.features')}</a>${doc('getting-started','nav.docs')}<a href="#download" data-i18n="nav.download">${t('nav.download')}</a></nav>
<div class="header-actions"><a class="github" href="${destinations.repository}">GitHub ↗</a><div class="language" role="group" data-i18n-label="nav.language" aria-label="${t('nav.language')}"><button type="button" data-lang="en" lang="en" aria-pressed="true">EN</button><button type="button" data-lang="fr" lang="fr" aria-pressed="false">FR</button></div><button class="menu" type="button" aria-controls="primary-nav" aria-expanded="false" data-i18n-label="nav.menu" aria-label="${t('nav.menu')}">☰</button></div>
</div></header>
<main id="main" tabindex="-1">
<section class="hero wrap" aria-labelledby="hero-title"><div class="hero-copy">
${text('p','hero.status','class="status"')}<h1 id="hero-title"><img src="${assets.logo}" width="48" height="48" alt=""><span>CyRevision</span></h1>
${text('h2','hero.lead','class="hero-lead"')}${text('p','hero.body')}
<div class="actions"><a class="button primary" href="#modes" data-i18n="hero.explore">${t('hero.explore')}</a>${doc('getting-started','hero.guide','button')}</div>${text('p','hero.note','class="fineprint"')}
</div><div class="hero-media">${shot('workspace',true)}</div></section>
<nav class="chapters" data-i18n-label="chapters.label" aria-label="${t('chapters.label')}"><div class="wrap">${chapters.map(id=>`<a data-chapter="${id}" href="#${id}" data-i18n="chapter.${id}">${t('chapter.'+id)}</a>`).join('')}</div></nav>
<section id="modes" class="section wrap" aria-labelledby="modes-title"><div class="section-head">${intro('modes')}</div>
<div class="cards">${['git','sync','backup'].map(id=>`<article>${text('h3','modes.'+id+'.title')}${text('p','modes.'+id+'.body')}</article>`).join('')}</div><div class="diagram-row">${shot('modes')}${doc('project-modes','modes.link')}</div></section>
<section id="history" class="section tinted" aria-labelledby="history-title"><div class="wrap feature-row"><div>${intro('history')}${list('history')}${doc('git-history','history.link')}</div>${shot('history')}</div></section>
<section id="assets" class="section wrap" aria-labelledby="assets-title"><div class="feature-row reverse"><div>${intro('assets')}${text('p','assets.note','class="fineprint"')}${doc('asset-diff','assets.link')}</div>${shot('diff')}</div></section>
<section id="review" class="section tinted" aria-labelledby="review-title"><div class="wrap feature-row"><div>${intro('review')}${list('review')}${text('p','review.note','class="fineprint"')}${doc('ci-actions','review.link')}</div>${shot('ci')}</div></section>
<section id="recovery" class="section wrap" aria-labelledby="recovery-title"><div class="section-head">${intro('recovery')}</div><div class="two-up">${['sync','backup'].map(id=>`<article>${text('h3',id+'.title')}${text('p',id+'.body')}${shot(id==='backup'?'backups':id)}${doc(id==='sync'?'sync-and-peers':'backups',id+'.link')}</article>`).join('')}</div></section>
<section id="tools" class="section tinted" aria-labelledby="tools-title"><div class="wrap">${text('p','tools.kicker','class="eyebrow"')}${text('h2','tools.title','id="tools-title"')}<div class="cards">${['code','ai','integrations'].map(id=>`<article>${text('h3','tools.'+id+'.title')}${text('p','tools.'+id+'.body')}</article>`).join('')}</div><div class="diagram-row">${shot('ai')}${doc('code-and-ai','tools.link')}</div></div></section>
<section id="manual" class="section wrap manual" aria-labelledby="manual-title"><div class="section-head">${intro('manual')}${text('p','manual.local','class="fineprint"')}</div><div class="cards manual-links">${doc('getting-started','manual.start','button')}${doc('keyboard-shortcuts','manual.keys','button')}${doc('plugin-api','manual.api','button')}</div></section>
<section class="section tinted" aria-labelledby="faq-title"><div class="wrap faq"><h2 id="faq-title" data-i18n="faq.title">${t('faq.title')}</h2><div>${[1,2,3,4].map(n=>`<details><summary data-i18n="faq.q${n}">${t('faq.q'+n)}</summary>${text('p','faq.a'+n)}</details>`).join('')}</div></div></section>
<section id="download" class="section wrap" aria-labelledby="download-title"><div class="section-head">${intro('download')}</div><div class="packages">${packages.map(([id,installer,portable])=>`<article>${text('h3','platform.'+id)}<a href="https://github.com/MrMybal/CyRevision/releases/download/v0.1.34/${installer}" data-i18n="package.${id}">${t('package.'+id)} ↗</a><a href="https://github.com/MrMybal/CyRevision/releases/download/v0.1.34/${portable}" data-i18n="package.portable">${t('package.portable')} ↗</a></article>`).join('')}</div>${text('p','download.note','class="fineprint"')}<div class="actions"><a class="button primary" href="${destinations.release}" data-i18n="download.release">${t('download.release')}</a><a class="button" href="${destinations.checksums}" data-i18n="download.checksums">${t('download.checksums')}</a></div></section>
</main>
<footer class="wrap site-footer"><div><a class="brand" href="#main"><img src="${assets.logo}" width="28" height="28" alt=""><span>CyRevision</span></a>${text('p','footer.line')}</div><nav data-i18n-label="nav.label" aria-label="${t('nav.label')}">${doc('getting-started','nav.docs')}<a href="${destinations.repository}">GitHub ↗</a><a href="./LICENSE" data-i18n="footer.license">${t('footer.license')}</a><a href="./NOTICES.txt" data-i18n="footer.notices">${t('footer.notices')}</a></nav></footer>
<dialog id="viewer" aria-labelledby="viewer-caption" aria-describedby="viewer-help"><div class="viewer-shell"><div class="viewer-top"><p id="viewer-caption"></p><button type="button" data-close data-i18n-label="viewer.close" aria-label="${t('viewer.close')}">✕</button></div><div class="viewer-image"><img id="viewer-image" alt=""></div><div class="viewer-bottom"><button type="button" data-prev data-i18n-label="viewer.previous" aria-label="${t('viewer.previous')}">←</button><span id="viewer-count" role="status" aria-live="polite"></span><button type="button" data-next data-i18n-label="viewer.next" aria-label="${t('viewer.next')}">→</button><p id="viewer-help" data-i18n="viewer.help">${t('viewer.help')}</p></div></div></dialog>
<noscript data-i18n="noscript">${t('noscript')}</noscript>
</body></html>`;
}
