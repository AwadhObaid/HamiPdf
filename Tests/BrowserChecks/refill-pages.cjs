// Verify a two-copy merge of 55.pdf: independent fields, Arabic refill, reopen.
// Usage: node refill-pages.cjs merged.pdf saved.pdf
const {chromium}=require('playwright');const fs=require('fs');const path=require('path');const http=require('http');
(async()=>{
 const server=http.createServer((req,res)=>{try{const f=path.join(__dirname,'../../Forms/Web',decodeURIComponent(req.url.split('?')[0]));res.setHeader('Content-Type',f.endsWith('.mjs')?'text/javascript':f.endsWith('.css')?'text/css':'text/html');res.end(fs.readFileSync(f));}catch{res.statusCode=404;res.end();}});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;
 try{
 browser=await chromium.launch({executablePath:process.env.HAMIPDF_CHROMIUM||undefined,headless:true,args:['--no-sandbox','--disable-dev-shm-usage']});
 const page=await browser.newPage({viewport:{width:1200,height:900}});
 await page.addInitScript(()=>{window.received=[];window.chrome={webview:{postMessage:m=>window.received.push(m),addEventListener:()=>{}}};});
 await page.goto(`http://127.0.0.1:${server.address().port}/index.html`);await page.waitForFunction(()=>window.hami);
 await page.evaluate(b=>window.hami.open(b),fs.readFileSync(process.argv[2]).toString('base64'));
 const first=page.locator('input[name="Hami1_1"]');await first.waitFor();
 await first.fill('عوض فغمه 789');await page.locator('#next').click();
 const second=page.locator('input[name="Hami2_1"]');await second.waitFor();
 if(await second.inputValue()==='عوض فغمه 789')throw Error('Duplicate form names linked across documents');
 await second.fill('Second independent value');await page.locator('#save').click();
 await page.waitForFunction(()=>window.received.some(m=>m.type==='save'),null,{timeout:30000});
 const saved=await page.evaluate(()=>window.received.find(m=>m.type==='save').data);
 fs.writeFileSync(process.argv[3],Buffer.from(saved,'base64'));
 const values=await page.evaluate(async data=>{
  const lib=await import('./pdfjs/build/pdf.mjs');const task=lib.getDocument({data:Uint8Array.from(atob(data),c=>c.charCodeAt(0)),isEvalSupported:false});
  try{const doc=await task.promise;const fields=await doc.getFieldObjects();return {count:fields.size,first:fields.get('Hami1_1')[0].value,second:fields.get('Hami2_1')[0].value};}finally{await task.destroy();}
 },saved);
 if(values.count!==140||values.first!=='عوض فغمه 789'||values.second!=='Second independent value')throw Error(JSON.stringify(values));
 console.log('PASS: 140 fields; Arabic refill, independent same-name fields, save and reopen.');
 await page.screenshot({path:process.argv[3]+'.png'});
 }finally{if(browser)await browser.close();server.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
