# WinForge

WinForge تطبيق Windows محلي ومفتوح المصدر للفحص والتنظيف والخصوصية والألعاب والطاقة والاسترجاع. لا يحتوي على Telemetry أو إعلانات أو حساب، ولا يستخدم Registry Cleaner أو تعديلات Timer وBCD الخرافية.

## المزايا الحالية

- واجهة WPF عربية RTL وإنجليزية LTR مع الوضع الداكن والفاتح.
- فحص حقيقي للنظام وبدء التشغيل والخدمات والمهام المجدولة وAppx وOptional Features وخطط الطاقة والشبكة.
- Cleaner من مرحلتين: Preview ثم Clean، مع Allowlist ومنع Junctions/Reparse Points وتجاوز الملفات المقفلة.
- Catalog بصيغة JSON مع فحص المخاطر والتوافق والتعارضات والاعتماديات والترجمة وSHA-256.
- Worker منفصل عبر Named Pipe محلي؛ لا يطلب UAC إلا عند وجود عملية إدارية.
- حفظ القيمة الأصلية ونوعها ووجودها، واسترجاع القيمة الغائبة بالحذف، وRollback جزئي عند فشل Batch.
- Logs منظمة وحزمة دعم بدون بيانات شخصية غير لازمة.

## البناء والتشغيل

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/package.ps1 -Runtime win-x64
./scripts/package-installer.ps1
```

ملف التطوير: `src/WinForge.UI/bin/Debug/net8.0-windows/WinForge.UI.exe`.

يحفظ الاسترجاع داخل `%ProgramData%\WinForge\Rollback\<SessionId>`. راجع [أمان التويكات](docs/tweak-safety.md) و[اختبار VM](docs/VM_TESTING.md).
