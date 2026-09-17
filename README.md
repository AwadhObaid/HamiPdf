# الحامي PDF — HamiPdf 0.9.2

تطبيق Windows لعرض ملفات PDF في تبويبات وتحريرها وتعبئة النماذج وإدارة صفحاتها.

المطوّر: **Awadh Faghmah** — Copyright © 2026.

## الجديد

- دمج وترتيب وتدوير واستخراج صفحات نماذج AcroForm مع حفظ الحقول وقيمها.
- فصل أسماء الحقول بين الملفات المدمجة لمنع تداخل القيم.
- إصلاح ترتيب تحرير عوارض PDF عند الإغلاق؛ أكد المستخدم نجاح تجربة 0.9.2 على Windows.
- معلومات التطبيق وأيقونته، والتثبيت في Program Files، واختبار محتوى الواجهة المنشورة.

## البناء والتشغيل

يتطلب Windows و.NET 8 SDK وWebView2 Runtime. من PowerShell داخل المشروع:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-run.ps1
```

ينفّذ السكربت التنظيف والاختبارات والبناء والتشغيل. بعد إغلاق البرنامج، ومع توفر Inno Setup:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-machine.ps1
```

راجع PHASE09_README_AR.md لحدود دعم النماذج وSHUTDOWN_FIX_AR.md لتفاصيل الإغلاق. إعداد تعطيل GPU في WebView2 إجراء توافق تجريبي. المستودع يتضمن نماذج اختبار اصطناعية فقط، ولا يتضمن مستندات المستخدم أو ملفات التثبيت الناتجة.
