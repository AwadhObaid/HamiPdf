# 0.9.2 — معالجة ترتيب الإغلاق

تحديث تجريبي لخطأ الخروج 0xC0000005 بعد إغلاق البرنامج يدويًا.
نُقل إيقاف وتحرير عارض PDF من حدث Closed إلى Closing، قبل تدمير نافذة WPF الأصلية، في النافذة الرئيسية ونافذة تعبئة النماذج. لم تتغير قرارات حفظ أو تجاهل التعديلات.
أضيف سجل مراحل الإغلاق في %LOCALAPPDATA%\HamiPdf\logs\shutdown_<PID>.log؛ عند خروج غير طبيعي يطبع build-run.ps1 هذا السجل وأحداث Windows الحديثة في سجل البناء نفسه. لا يجري تحويل رمز الخطأ إلى نجاح.

اجتاز بناء Windows-targeted WPF هنا. لم يمكن اختبار انهيار تعريف AMD على بيئة Linux. لا ندّعي تأكيد سبب الانهيار أو إصلاحه النهائي قبل تجربة الجهاز المتأثر.
إعدادات العرض البرمجي التجريبية من 0.9.1 باقية.

للتجربة: افتح البرنامج وملف PDF، افتح تعبئة النماذج واكتب قيمة ثم أغلقها واختر تجاهل التعديلات. أغلق البرنامج الرئيسي. النتيجة المطلوبة في PowerShell:
[OK] Application closed normally (exit code 0).
إذا ظهر الخطأ أرسل سجل logs الجديد كاملًا.

الأوامر: فك الحزمة منفصلة وأغلق البرنامج وVisual Studio، ثم:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -ProjectRoot "E:\C Sharp Projects\HamiPdf"
cd "E:\C Sharp Projects\HamiPdf"
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-run.ps1
```

بعد نجاح اختبار الإغلاق، أنشئ المثبّت وحدّث نسخة سطح المكتب:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-machine.ps1
```
