# Third-party components

PDF.js (pdfjs-dist) 6.3.289, Mozilla Foundation, Apache License 2.0.
https://github.com/mozilla/pdf.js
https://www.npmjs.com/package/pdfjs-dist/v/6.3.289
Bundled locally in Forms/Web/pdfjs; no CDN is used at runtime.
The compatibility (legacy) pdf.mjs, pdf.worker.mjs and pdf_viewer.mjs builds are used unchanged.
PDF.js license: Forms/Web/pdfjs/LICENSE.
Font, CMap, ICC and WASM component notices remain in their respective folders.

PDFsharp 6.2.4 and Microsoft WebView2 remain the existing NuGet dependencies.


Scanner integration:
NAPS2.Sdk / Images.Wpf / Sdk.Worker.Win32 1.3.0, Copyright NAPS2 Contributors,
LGPL 2.1 or later; unchanged dynamically linked packages, no NAPS2 application branding.
License: Licenses/NAPS2-SDK-LICENSE.txt.
Source: https://github.com/cyanfish/naps2/tree/8ae3e82203115754e804fe9c14f00f6bd86ee192
SDK docs: https://www.naps2.com/sdk/doc/api/
The same SDK source revision supplies unchanged lib/win32/twaindsm.dll and lib/win64/twaindsm.dll, packaged as _win32/twaindsm.dll and _win64/twaindsm.dll to match SDK lookup.
TWAIN DSM, Copyright TWAIN Working Group, LGPL 2.1 or later.
Source: https://github.com/twain/twain-dsm
Licenses/TWAIN-DSM-LGPL.txt and Licenses/SCANNER-SOURCES.txt.
These libraries remain replaceable separate DLLs; no trimming or single-file bundling is used.
