import * as pdfjs from './pdfjs/build/pdf.mjs';
globalThis.pdfjsLib = pdfjs;
const { PDFViewer, EventBus, PDFLinkService } = await import('./pdfjs/web/pdf_viewer.mjs');
pdfjs.GlobalWorkerOptions.workerSrc = './pdfjs/build/pdf.worker.mjs';
const el = id => document.getElementById(id);
const post = message => window.chrome?.webview?.postMessage(message);
const status = text => { el('status').textContent = text; };
const eventBus = new EventBus();
const linkService = new PDFLinkService({ eventBus, externalLinkEnabled: false });
const viewer = new PDFViewer({container:el('container'),viewer:el('viewer'),eventBus,linkService,
    annotationMode:pdfjs.AnnotationMode.ENABLE_FORMS,annotationEditorMode:pdfjs.AnnotationEditorType.DISABLE,
    enablePermissions:true,enableAutoLinking:false,imageResourcesPath:'./pdfjs/web/images/'});
linkService.setViewer(viewer);
let pdf, fields, saving = false, dirty = false;
const local = path => new URL(path,window.location.href).href;
const options = {isEvalSupported:false,enableXfa:false,cMapUrl:local('./pdfjs/cmaps/'),cMapPacked:true,
    standardFontDataUrl:local('./pdfjs/standard_fonts/'),wasmUrl:local('./pdfjs/wasm/'),iccUrl:local('./pdfjs/iccs/')};
function changed() { if (!dirty) { dirty=true; post({type:'dirty'}); } status('تعديلات غير محفوظة · احفظ نسخة قبل الإغلاق'); }
eventBus.on('pagesinit',()=> { viewer.currentScaleValue = el('zoom').value; });
eventBus.on('pagechanging',()=> updatePages());
function updatePages() { el('pages').textContent=`${viewer.currentPageNumber} / ${pdf.numPages}`; el('prev').disabled=viewer.currentPageNumber<=1; el('next').disabled=viewer.currentPageNumber>=pdf.numPages; }
el('prev').onclick=()=>viewer.currentPageNumber--;
el('next').onclick=()=>viewer.currentPageNumber++;
el('zoom').onchange=()=>{viewer.currentScaleValue=el('zoom').value;};
// Capture input immediately, including typing before blur or attempting to close the window.
el('viewer').addEventListener('input',changed);
el('viewer').addEventListener('change',changed);
function decode(text) { return Uint8Array.from(atob(text), x=>x.charCodeAt(0)); }
function encode(data) { let text=''; for(let i=0;i<data.length;i+=32768) text+=String.fromCharCode(...data.subarray(i,i+32768)); return btoa(text); }
function normalized(value) { return JSON.stringify(Array.isArray(value)?value:[value ?? '']); }
async function validate(bytes, stored) {
    const task=pdfjs.getDocument({...options,data:bytes.slice()});
    const check=await task.promise;
    try {
        const after=Object.fromEntries(await check.getFieldObjects() || []);
        if(check.numPages!==pdf.numPages || JSON.stringify(Object.keys(after||{}).sort())!==JSON.stringify(Object.keys(fields).sort())) throw Error('لم ينجح التحقق من بقاء جميع الحقول والصفحات.');
        for(const [name,before] of Object.entries(fields)) {
            const next=after[name];
            if(next.length!==before.length) throw Error('تغيّر عدد عناصر أحد الحقول.');
            for(let i=0;i<before.length;i++) {
                const a=before[i], b=next.find(x=>x.id===a.id);
                if(!b || a.type!==b.type || a.page!==b.page || normalized(a.rect)!==normalized(b.rect)) throw Error('تغيّرت بنية حقل بعد الحفظ.');
                const value=stored[a.id]?.value;
                // Radio groups have a shared value: an unchecked widget may correspond to a selected sibling.
                let expected=a.value;
                if(a.type==='radiobutton') {
                    const chosen=before.find(x=>stored[x.id]?.value===true);
                    if(chosen) expected=chosen.exportValues;
                    else if(before.some(x=>stored[x.id]?.value===false)) expected='Off';
                } else if(value!==undefined) expected=(a.type==='checkbox')?(value?a.exportValues:'Off'):value;
                if(normalized(expected)!==normalized(b.value)) throw Error('لم ينجح التحقق من قيمة الحقل: '+name);
            }
        }
    } finally { await task.destroy(); }
}
async function save() {
    if(!pdf || saving || el('save').disabled) return;
    document.activeElement?.blur();
    await new Promise(r=>setTimeout(r,0));
    saving=true;document.body.classList.add('saving');el('save').disabled=true;el('container').inert=true;
    post({type:'dirty'}); // Keep the host conservative until it confirms a successful disk write.
    try {
        status('جارٍ حفظ الحقول والتحقق منها…');
        const stored=Object.fromEntries(Object.values(fields).flat().map(f=>[f.id, structuredClone(pdf.annotationStorage.getRawValue(f.id) || {})]));
        const bytes=await pdf.saveDocument();
        await validate(bytes,stored);
        post({type:'save',data:encode(bytes)});
    } catch(e) { complete(false,'تعذر حفظ النموذج: '+e.message); }
}
function complete(ok,message) {
    saving=false;el('save').disabled=false;el('container').inert=false;document.body.classList.remove('saving');
    if(ok) dirty=false;
    status(message);
}
el('save').onclick=save;
document.addEventListener('keydown',e=>{if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='s'){e.preventDefault();save();}});
window.chrome?.webview?.addEventListener('message',e=>{if(e.data.type==='saved')complete(e.data.ok,e.data.message);});
window.hami={open:async base64=>{
    try {
        pdf=await pdfjs.getDocument({...options,data:decode(base64)}).promise;
        const metadata=await pdf.getMetadata();
        if(metadata.info.IsXFAPresent) throw Error('نماذج XFA غير مدعومة في هذه المرحلة.');
        fields=Object.fromEntries(await pdf.getFieldObjects() || []);
        if(!fields || !Object.keys(fields).length) throw Error('لا يحتوي الملف حقول نموذج قابلة للتعبئة.');
        if(Object.values(fields).flat().some(x=>x.type==='signature')) throw Error('هذا الملف يحتوي حقول توقيع رقمي؛ لا يدعم محرر النماذج حفظه حاليًا.');
        const permissions=await pdf.getPermissions();
        if(permissions&&!permissions.has(pdfjs.PermissionFlag.FILL_INTERACTIVE_FORMS)&&!permissions.has(pdfjs.PermissionFlag.MODIFY_ANNOTATIONS)) throw Error('صلاحيات هذا المستند لا تسمح بتعبئة الحقول.');
        pdf.annotationStorage.onSetModified=changed;
        viewer.setDocument(pdf);linkService.setDocument(pdf);
        el('save').disabled=false;updatePages();
        status(`${Object.keys(fields).length} حقل · اكتب داخل النموذج ثم اضغط «حفظ نسخة»`);
    } catch(e) { status('تعذر فتح النموذج: '+e.message); }
}};
post({type:'ready'});
