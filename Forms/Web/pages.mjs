import { getDocument, GlobalWorkerOptions } from './pdfjs/build/pdf.mjs';
GlobalWorkerOptions.workerSrc = './pdfjs/build/pdf.worker.mjs';
const decode = data => Uint8Array.from(atob(data), c => c.charCodeAt(0));
const encode = bytes => {
  let text = '';
  for (let i = 0; i < bytes.length; i += 32768) text += String.fromCharCode(...bytes.subarray(i, i + 32768));
  return btoa(text);
};
// One entry per source preserves shared fields across that source's selected pages.
// PDF.js includePages follows original page order; pageIndices supplies output order.
export async function compose(sources) {
  if (!sources.length) throw Error('لا توجد صفحات للحفظ.');
  let task;
  try {
    const inputs = sources.map(s => ({ ...s, bytes: decode(s.data) }));
    task = getDocument({ data: inputs[0].bytes.slice(), isEvalSupported: false, enableXfa: false });
    task.onPassword = () => { task.destroy(); };
    const primary = await task.promise;
    const infos = inputs.map((s, i) => ({
      document: i === 0 ? null : s.bytes,
      includePages: s.pages.map(p => p.sourcePage),
      pageIndices: s.pages.map(p => p.outputPage)
    }));
    const result = await primary.extractPages(infos);
    if (!result?.length) throw Error('تعذر نقل الصفحات وحقولها.');
    return encode(result);
  } finally { if (task) await task.destroy(); }
}
window.hamiPages = {
  async run(sources) {
    try { const data = await compose(sources); window.chrome.webview.postMessage({ok:true, data}); }
    catch (e) { window.chrome.webview.postMessage({ok:false, error:String(e.message || e)}); }
  }
};
window.chrome?.webview?.postMessage({ready:true});
