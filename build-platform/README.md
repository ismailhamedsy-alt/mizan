# منصة بناء الميزان Windows

- Target: `net10.0-windows`
- SDK: `10.0.112`
- SQLite provider: `Microsoft.Data.Sqlite 10.0.12`
- Platform: `win-x64`, self-contained.

## البناء المحلي
1. ثبّت .NET SDK 10.0.112.
2. شغّل `1-BUILD-MIZAN.cmd` من جذر المستودع.
3. الناتج `desktop\mizan\dist\Mizan.exe`.
4. السجل `desktop\mizan\dist\build.log`.

## فحوصات CI
تشمل فحص XML لكل XAML ومعالجات الأحداث، وفحص التنقل والتمرير والتباين، وRestore/Build/Publish، وفحص PE، واختبارات الدخول الجديد والحساب القديم والاستعادة، وإنشاء كل الواجهات الأساسية وإجراء Measure/Arrange لها، ثم اختبار التشغيل الطبيعي.
