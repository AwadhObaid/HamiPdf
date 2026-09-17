# الحامي PDF 0.8.2 — حول التطبيق ووضوح الشعار

أُضيف زر «حول التطبيق» في الشريط العلوي، يعرض وصف البرنامج ورقم الإصدار الفعلي واسم المطور Awadh Faghmah وحقوقه. يظهر اسم المطور أيضًا في معلومات ملف البرنامج والمثبّت.

الشريط كان يستخدم ICO متعدد المقاسات داخل Image بحجم 26 بكسل. الآن يستخدم الصورة الأصلية PNG بدقة 1254×1254 مع تحجيم عالي الجودة ومحاذاة إلى البكسل، داخل مساحة 28 بكسل. بقي تصميم الأيقونة المعتمد وملف ICO الخاص بويندوز دون تغيير. تظل التفاصيل الدقيقة محدودة بطبيعتها في أيقونة صغيرة.

أغلق البرنامج وVisual Studio وفك الحزمة منفصلة. من جوار install.ps1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -ProjectRoot "E:\C Sharp Projects\HamiPdf"
cd "E:\C Sharp Projects\HamiPdf"
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-run.ps1
```

جرّب «حول التطبيق» وافحص وضوح الشعار على مقياس شاشة 100% و125% إن أمكن. ثم أغلق البرنامج لتحديث النسخة المثبّتة:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

بعد نجاح البناء، من PowerShell عادي:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-machine.ps1
```

إذا كانت النسخة الحالية شخصية، سيفتح السكربت مزيلها قبل تشغيل التثبيت الجديد. إذا كانت مثبتة بالفعل لجميع المستخدمين، يشغّل المثبّت لتحديثها. يبقى المسار Program Files\HamiPdf. جرى الاحتفاظ بتصحيح Inno Setup واشتراط ملف SHA256 للبناء الناجح.
