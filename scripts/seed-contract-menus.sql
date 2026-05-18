/*
  منو و دسترسی «گردش قرارداد» — روی دیتابیس porsokhan (یا همان DB پنل) اجرا کنید.
  بعد از اجرا: از پنل خارج شوید و دوباره وارد شوید.
*/
SET NOCOUNT ON;

DECLARE @ContractsParentId UNIQUEIDENTIFIER;

IF NOT EXISTS (SELECT 1 FROM MenuItems WHERE [Key] = N'contracts')
BEGIN
    SET @ContractsParentId = NEWID();
    INSERT INTO MenuItems (Id, [Key], Title, Icon, IconColor, Route, [Order], ParentId)
    VALUES (@ContractsParentId, N'contracts', N'گردش قرارداد', N'FileText', N'#4F46E5', NULL, 3, NULL);
END
ELSE
    SELECT @ContractsParentId = Id FROM MenuItems WHERE [Key] = N'contracts';

IF NOT EXISTS (SELECT 1 FROM MenuItems WHERE [Key] = N'contracts.list')
    INSERT INTO MenuItems (Id, [Key], Title, Icon, IconColor, Route, [Order], ParentId)
    VALUES (NEWID(), N'contracts.list', N'لیست قراردادها', N'FileText', N'#4F46E5', N'/admin/contracts', 1, @ContractsParentId);

IF NOT EXISTS (SELECT 1 FROM MenuItems WHERE [Key] = N'contracts.settings')
    INSERT INTO MenuItems (Id, [Key], Title, Icon, IconColor, Route, [Order], ParentId)
    VALUES (NEWID(), N'contracts.settings', N'تنظیمات قرارداد', N'Settings2', N'#6366F1', N'/admin/contracts/settings', 2, @ContractsParentId);

-- Permissions
DECLARE @PermNames TABLE (Name NVARCHAR(120));
INSERT INTO @PermNames (Name) VALUES
 (N'contracts.read'), (N'contracts.add'), (N'contracts.update'),
 (N'contracts.settings.read'), (N'contracts.settings.update');

INSERT INTO Permissions (Id, Name)
SELECT NEWID(), n.Name
FROM @PermNames n
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Name = n.Name);

-- RolePermissions برای Admin و Expert
DECLARE @AdminRoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM AspNetRoles WHERE Name = N'Admin');
DECLARE @ExpertRoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM AspNetRoles WHERE Name = N'Expert');

IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO RolePermissions (RoleId, PermissionId)
    SELECT @AdminRoleId, p.Id
    FROM Permissions p
    WHERE p.Name IN (SELECT Name FROM @PermNames)
      AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = @AdminRoleId AND rp.PermissionId = p.Id);
END

IF @ExpertRoleId IS NOT NULL
BEGIN
    INSERT INTO RolePermissions (RoleId, PermissionId)
    SELECT @ExpertRoleId, p.Id
    FROM Permissions p
    WHERE p.Name IN (N'contracts.read', N'contracts.add', N'contracts.update')
      AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = @ExpertRoleId AND rp.PermissionId = p.Id);
END

-- RoleMenus: حتماً والد (contracts) + فرزندها برای هر نقش
DECLARE @MenuKeys TABLE ([Key] NVARCHAR(80));
INSERT INTO @MenuKeys VALUES (N'contracts'), (N'contracts.list'), (N'contracts.settings');

IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO RoleMenus (RoleId, MenuId)
    SELECT @AdminRoleId, m.Id
    FROM MenuItems m
    INNER JOIN @MenuKeys k ON k.[Key] = m.[Key]
    WHERE NOT EXISTS (SELECT 1 FROM RoleMenus rm WHERE rm.RoleId = @AdminRoleId AND rm.MenuId = m.Id);
END

IF @ExpertRoleId IS NOT NULL
BEGIN
    INSERT INTO RoleMenus (RoleId, MenuId)
    SELECT @ExpertRoleId, m.Id
    FROM MenuItems m
    WHERE m.[Key] IN (N'contracts', N'contracts.list')
      AND NOT EXISTS (SELECT 1 FROM RoleMenus rm WHERE rm.RoleId = @ExpertRoleId AND rm.MenuId = m.Id);
END

PRINT N'منو و دسترسی قرارداد ثبت شد. یک بار logout/login کنید.';

SELECT [Key], Title, Route FROM MenuItems WHERE [Key] LIKE N'contracts%';
