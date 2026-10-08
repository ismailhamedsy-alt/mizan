# تشغيل الميزان على Windows

بعد نجاح البناء ستجد التطبيق هنا:

`desktop\mizan\dist\Mizan.exe`

ويمكنك تشغيله مباشرة بالنقر المزدوج.

كما يوجد:
- `dist\تشغيل-الميزان.cmd`
- `dist\Mizan-Windows-x64.zip`

السكربت `build-windows.cmd` يحذف مجلدات bin/obj/publish/dist القديمة قبل كل بناء، ثم:
1. Restore
2. Build Release
3. Publish self-contained win-x64
4. ينتج Mizan.exe واحدًا
5. ينشئ ZIP للتوزيع

إذا فشل البناء، شغّل `diagnose-build.cmd` وانسخ لي آخر 30 سطر من الناتج.
