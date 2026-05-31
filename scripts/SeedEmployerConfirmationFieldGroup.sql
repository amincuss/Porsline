-- قالب آماده «تأییدیه کارفرما» — معمولاً با RunSeed در API اعمال می‌شود.
-- شناسه ثابت: 7c4e9a20-8f1b-4a3d-9c2e-1a0b5c6d7e8f
-- برای اعمال دستی پس از seed C#، از API استفاده کنید یا RunSeed=true را در appsettings فعال کنید.

IF NOT EXISTS (SELECT 1 FROM [dbo].[FormFieldGroupTemplates] WHERE [Id] = '7c4e9a20-8f1b-4a3d-9c2e-1a0b5c6d7e8f')
BEGIN
    PRINT N'قالب در دیتابیس نیست — API را با RunSeed=true ری‌استارت کنید.';
END
ELSE
BEGIN
    PRINT N'قالب «تأییدیه کارفرما (تأمین اجتماعی)» موجود است.';
END
