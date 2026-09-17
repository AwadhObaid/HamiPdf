# الحامي PDF — HamiPdf

تطبيق Windows لعرض ملفات PDF في تبويبات، وتنظيم الصفحات، وإضافة النصوص والصور والملاحظات والتظليل والرسم، وحفظ مشاريع قابلة للاستكمال. يدعم تعبئة نماذج AcroForm وحفظها تفاعليًا.

**المطور: Awadh Faghmah**  
**الإصدار: 0.8.2**

## المتطلبات

Windows 10 (19041 أو أحدث) أو Windows 11، و.NET 8 SDK للبناء، وWebView2 Runtime للعرض. إنشاء ملف التثبيت يحتاج Inno Setup 6.3 أو أحدث.

## التنظيف والاختبارات والتشغيل

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-run.ps1
```

## إنشاء المثبّت

أغلق البرنامج أولًا:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

يستخدم المثبّت Program Files\HamiPdf لجميع المستخدمين. راجع PROGRAM_FILES_AR.md للانتقال من نسخة مستخدم واحد، وUPDATE_082_AR.md لأحدث التغييرات.

## رفع هذه الحزمة من جهازك

افتح PowerShell بجوار upload-github.ps1 داخل مجلد الحزمة الجديد، ثم نفّذ:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\upload-github.ps1
```

يرفع السكربت إلى المستودع الذي حدّده المستخدم AwadhObaid/HamiPdf ويحافظ على ظهوره الحالي. يحتاج Git وGitHub CLI؛ يسجّل الدخول بحساب AwadhObaid إن لزم. لا يستخدم force push ويتوقف عند اختلاف تاريخ المستودع. شغّله داخل هذه الحزمة النظيفة، وليس داخل مجلد يضم ملفات شخصية غير مراجعة.

## استنساخ المشروع والتحديثات

```powershell
git clone https://github.com/AwadhObaid/HamiPdf.git
cd HamiPdf
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-run.ps1
```

ملفات البناء والمستندات الشخصية ومفاتيح التوقيع مستبعدة بواسطة .gitignore. لإرسال تحديثاتك بعد مراجعتها:

```powershell
git add .
git commit -m "Update HamiPdf"
git push origin main
```

## حدود الدعم

إدارة صفحات النماذج التفاعلية (الدمج والاستخراج والترتيب) مؤجلة. XFA والتوقيعات الرقمية وحسابات JavaScript داخل PDF غير مدعومة حاليًا. راجع PHASE08_README_AR.md وVALIDATION.md لتفاصيل الاختبارات وحدود التحقق.

## الحقوق

Copyright © 2026 Awadh Faghmah. All rights reserved.

المكونات الخارجية تحتفظ بتراخيصها؛ راجع THIRD_PARTY_NOTICES.md والتراخيص المرفقة بمحرك PDF.js.
