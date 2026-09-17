// Usage: node pages.cjs prepared.json composed.pdf
const {chromium}=require('playwright');const fs=require('fs');const path=require('path');const http=require('http');
(async()=>{
 const server=http.createServer((req,res)=>{const file=path.join(__dirname,'../../Forms/Web',decodeURIComponent(req.url.split('?')[0]));try{res.setHeader('Content-Type',file.endsWith('.mjs')?'text/javascript':'text/html');res.end(fs.readFileSync(file));}catch{res.statusCode=404;res.end();}});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;
 try{
 browser=await chromium.launch({...(process.env.HAMIPDF_CHROMIUM?{executablePath:process.env.HAMIPDF_CHROMIUM}:{}),headless:true,args:['--no-sandbox','--disable-dev-shm-usage']});
 const page=await browser.newPage();page.on('console',m=>{if(m.type()==='warning'||m.type()==='error')console.log(m.type(),m.text())});
 await page.goto(`http://127.0.0.1:${server.address().port}/pages.html`);
 const plan=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
 const data=await page.evaluate(async sources=>(await import('./pages.mjs')).compose(sources),plan.Sources);
 fs.writeFileSync(process.argv[3],Buffer.from(data,'base64'));
 console.log('PASS: browser document composition');
 }finally{if(browser)await browser.close();server.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
