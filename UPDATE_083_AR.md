# 0.9.2 — تصحيح الواجهة القديمة داخل المثبّت

الصورة على جهاز المستخدم أظهرت غياب زر «حول التطبيق» رغم إصدار EXE رقم 0.8.2. لا تكفي بيانات EXE للتأكد من محتوى الواجهة في DLL. كان سكربتنا ينظف Debug فقط قبل نشر Release، ولذلك قد يبقى ناتج WPF سابق عند نسخ ملفات بتاريخ أقدم. لا تتوفر ملفات DLL من جهاز المستخدم للجزم بسبب الحالة.

التصحيح:
- نقل جميع ملفات الواجهة والتحقق من مطابقة نسخها بواسطة SHA256.
- حذف مجلدي البناء المولدين obj/Release وbin/Release قبل النشر.
- تشغيل البرنامج المنشور بوضع فحص دون إظهار نوافذه أو فتح مستندات؛ يحمّل الواجهة المترجمة ويشترط وجود زر «حول التطبيق» واسم Awadh Faghmah وصورة عرض بعرض 1000 بكسل على الأقل ورقم الإصدار الصحيح.
- يفشل بناء المثبّت إذا فشل هذا الفحص. يُحفظ تقرير ui-verification.json مع الملفات المنشورة.
- بعد التثبيت يقارن سكربت install-machine.ps1 بصمة DLL المثبّت ببصمة النسخة التي اجتازت فحص الواجهة.

## التنفيذ

أغلق الحامي PDF وVisual Studio. فك الحزمة الجديدة في مجلد منفصل وافتح PowerShell عاديًا بجانب install.ps1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -ProjectRoot "E:\C Sharp Projects\HamiPdf"
cd "E:\C Sharp Projects\HamiPdf"
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

ينفذ هذا التنظيف والاختبارات والبناء. يجب أن يظهر:

[OK] Published UI: About button, developer credit and high-resolution PNG verified.

ثم Successful compile و[OK] Setup. بعدها فقط:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-machine.ps1
Start-Process "C:\Program Files\HamiPdf\HamiPdf.exe"
```

للتنظيف والتشغيل من المصدر عند الحاجة:

```powershell
cd "E:\C Sharp Projects\HamiPdf"
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-run.ps1
```

لا تستخدم المثبّت السابق 0.8.2 لهذا التصحيح. المثبّت المطلوب HamiPdf-Setup-0.9.2-win-x64.exe. إذا ظهرت رسالة فشل، أرسلها ولا تتجاوز الفحص.

تم بناء المشروع هنا باستهداف Windows، لكن تشغيل فحص WPF المنشور وInno Setup يحتاج جهاز Windows. نسخة هذه الحزمة تحتوي زر حول التطبيق وصورة PNG الأصلية؛ يلزم فحص العرض الفعلي بعد التثبيت.
