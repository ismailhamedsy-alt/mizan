# Mizan Windows Build Platform

هذه هي منصة البناء الموحدة للمشروع.

## Local Windows
1. ثبّت .NET SDK 8.0.413.
2. شغّل `1-BUILD-MIZAN.cmd` من جذر المشروع.
3. الناتج النهائي: `desktop\mizan\dist\Mizan.exe`.
4. التشغيل: `2-RUN-MIZAN.cmd`.
5. الاختبار السريع: `build-platform\SmokeTest.ps1`.
6. السجل: `desktop\mizan\dist\build.log`.

## CI
يوجد workflow في `.github/workflows/windows-build.yml` يبني على Windows runner، ويتحقق من EXE، ويرفع Artifact باسم `Mizan-Windows-x64`.

لا يعتبر الإصدار ناجحًا إلا بعد نجاح RESTORE + BUILD + PUBLISH + PE validation.
