// Optional loopback-only preview; opening dist/website/index.html needs no server.
import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import {output,verifyArtifact} from './build.mjs';
await verifyArtifact();
const port=Number(process.env.CYREVISION_SHOWCASE_PORT||4173);
if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Choose a port between 1024 and 65535.');
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.json':'application/json; charset=utf-8','.svg':'image/svg+xml','.png':'image/png','.txt':'text/plain; charset=utf-8','.md':'text/plain; charset=utf-8'};
const server=http.createServer(async(req,res)=>{
  try {
    if(!['GET','HEAD'].includes(req.method)){res.writeHead(405).end();return;}
    const url=new URL(req.url,'http://127.0.0.1'),prefix='/cyrevision/';
    if(url.pathname==='/'){res.writeHead(302,{Location:prefix}).end();return;}
    if(!url.pathname.startsWith(prefix)){res.writeHead(404).end();return;}
    const requested=decodeURIComponent(url.pathname.slice(prefix.length)),relative=requested.endsWith('/')||requested===''?requested+'index.html':requested;
    const file=path.resolve(output,relative);
    if(!file.startsWith(output+path.sep)){res.writeHead(403).end();return;}
    if((await fs.lstat(file)).isSymbolicLink()||!(await fs.stat(file)).isFile()){res.writeHead(404).end();return;}
    if(!(await fs.realpath(file)).startsWith(await fs.realpath(output)+path.sep)){res.writeHead(403).end();return;}
    const data=await fs.readFile(file),ext=path.extname(file);
    res.writeHead(200,{'Content-Type':types[ext]||'application/octet-stream','X-Content-Type-Options':'nosniff','Cache-Control':/\.[a-f0-9]{12}\./.test(file)?'public,max-age=31536000,immutable':'no-cache'});
    res.end(req.method==='HEAD'?undefined:data);
  }catch{res.writeHead(404).end();}
});
server.listen(port,'127.0.0.1',()=>console.log('Local preview: http://127.0.0.1:'+port+'/cyrevision/ (Ctrl+C to stop; not published)'));
