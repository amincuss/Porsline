using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleMenu> RoleMenus => Set<RoleMenu>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<ResponderOtpCode> ResponderOtpCodes => Set<ResponderOtpCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SecuritySettings> SecuritySettings => Set<SecuritySettings>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<SmsSettings> SmsSettings => Set<SmsSettings>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<Responder> Responders => Set<Responder>();
    public DbSet<ResponderGroup> ResponderGroups => Set<ResponderGroup>();
    public DbSet<ResponderGroupMember> ResponderGroupMembers => Set<ResponderGroupMember>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<UserGroupMember> UserGroupMembers => Set<UserGroupMember>();
    public DbSet<UserPosition> UserPositions => Set<UserPosition>();
    public DbSet<Form> Forms => Set<Form>();
    public DbSet<FormField> FormFields => Set<FormField>();
    public DbSet<FormFieldGroupTemplate> FormFieldGroupTemplates => Set<FormFieldGroupTemplate>();
    public DbSet<FormWordTemplate> FormWordTemplates => Set<FormWordTemplate>();
    public DbSet<FormSubmissionWordDocument> FormSubmissionWordDocuments => Set<FormSubmissionWordDocument>();
    public DbSet<FormWordBatchExportJob> FormWordBatchExportJobs => Set<FormWordBatchExportJob>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
    public DbSet<FormDispatchLink> FormDispatchLinks => Set<FormDispatchLink>();
    public DbSet<FormUserAccess> FormUserAccesses => Set<FormUserAccess>();
    public DbSet<FormWorkflowTemplate> FormWorkflowTemplates => Set<FormWorkflowTemplate>();
    public DbSet<FormSubmissionApprovalLink> FormSubmissionApprovalLinks => Set<FormSubmissionApprovalLink>();
    public DbSet<FormActionLink> FormActionLinks => Set<FormActionLink>();
    public DbSet<ContractType> ContractTypes => Set<ContractType>();
    public DbSet<ContractSettings> ContractSettings => Set<ContractSettings>();
    public DbSet<ContractWorkflowTemplate> ContractWorkflowTemplates => Set<ContractWorkflowTemplate>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractVersion> ContractVersions => Set<ContractVersion>();
    public DbSet<ContractTextIndex> ContractTextIndexes => Set<ContractTextIndex>();
    public DbSet<ContractApprovalLink> ContractApprovalLinks => Set<ContractApprovalLink>();
    public DbSet<ContractActionLink> ContractActionLinks => Set<ContractActionLink>();
    public DbSet<ContractDocumentTemplate> ContractDocumentTemplates => Set<ContractDocumentTemplate>();
    public DbSet<ContractDocumentTemplateVersion> ContractDocumentTemplateVersions => Set<ContractDocumentTemplateVersion>();
    public DbSet<ContractDocumentTemplateField> ContractDocumentTemplateFields => Set<ContractDocumentTemplateField>();
    public DbSet<DocumentFolder> DocumentFolders => Set<DocumentFolder>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<DocumentSystemTag> DocumentSystemTags => Set<DocumentSystemTag>();
    public DbSet<DocumentSystemCategory> DocumentSystemCategories => Set<DocumentSystemCategory>();
    public DbSet<DocumentSystemOrganizationalUnit> DocumentSystemOrganizationalUnits => Set<DocumentSystemOrganizationalUnit>();
    public DbSet<DocumentSystemProject> DocumentSystemProjects => Set<DocumentSystemProject>();
    public DbSet<DocumentActivity> DocumentActivities => Set<DocumentActivity>();
    public DbSet<DocumentPermissionConfig> DocumentPermissionConfigs => Set<DocumentPermissionConfig>();
    public DbSet<DocumentPermissionEntry> DocumentPermissionEntries => Set<DocumentPermissionEntry>();
    public DbSet<DocumentShareLink> DocumentShareLinks => Set<DocumentShareLink>();
    public DbSet<DocumentVersionText> DocumentVersionTexts => Set<DocumentVersionText>();
    public DbSet<DocumentWorkflowTemplate> DocumentWorkflowTemplates => Set<DocumentWorkflowTemplate>();
    public DbSet<DocumentApprovalLink> DocumentApprovalLinks => Set<DocumentApprovalLink>();
    public DbSet<DocumentRetentionPolicy> DocumentRetentionPolicies => Set<DocumentRetentionPolicy>();
    public DbSet<DocumentLifecycleSettings> DocumentLifecycleSettings => Set<DocumentLifecycleSettings>();
    public DbSet<DocumentPublicProfile> DocumentPublicProfiles => Set<DocumentPublicProfile>();
    public DbSet<PublicCategory> PublicCategories => Set<PublicCategory>();
    public DbSet<PublicCollection> PublicCollections => Set<PublicCollection>();
    public DbSet<PublicBanner> PublicBanners => Set<PublicBanner>();
    public DbSet<PublicPortalSettings> PublicPortalSettings => Set<PublicPortalSettings>();
    public DbSet<PublicAnalyticsEvent> PublicAnalyticsEvents => Set<PublicAnalyticsEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.NationalCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.PhoneNumber).HasMaxLength(11);
            entity.Property(x => x.AvatarUrl).HasMaxLength(500);
            entity.Property(x => x.AboutMe).HasMaxLength(1000);
            entity.Property(x => x.SignatureImagePath).HasMaxLength(500);
            entity.Property(x => x.SignatureDisplayDegree).HasDefaultValue(60);
            entity.Property(x => x.PersonnelCode).HasMaxLength(30);
            entity.HasIndex(x => x.PersonnelCode)
                .IsUnique()
                .HasFilter("[PersonnelCode] IS NOT NULL AND [PersonnelCode] <> ''");
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasIndex(x => x.NationalCode).IsUnique();
            entity.HasIndex(x => x.PhoneNumber).IsUnique();
            entity.HasOne(x => x.UserPosition)
                .WithMany()
                .HasForeignKey(x => x.UserPositionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<UserPosition>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        builder.Entity<MenuItem>(entity =>
        {
            entity.Property(x => x.Key).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Icon).HasMaxLength(50).IsRequired();
            entity.Property(x => x.IconColor).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Route).HasMaxLength(250);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => new { x.ParentId, x.Order });
        });

        builder.Entity<Permission>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(x => new { x.RoleId, x.PermissionId });
            entity.HasOne(x => x.Permission)
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RoleMenu>(entity =>
        {
            entity.HasKey(x => new { x.RoleId, x.MenuId });
            entity.HasOne<AppRole>()
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MenuItem>()
                .WithMany()
                .HasForeignKey(x => x.MenuId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OtpCode>(entity =>
        {
            entity.Property(x => x.MobileNumber).HasMaxLength(11).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(6).IsRequired();
            entity.HasIndex(x => new { x.MobileNumber, x.Code, x.IsUsed });
            entity.HasIndex(x => x.ExpiresAtUtc);
        });

        builder.Entity<ResponderOtpCode>(entity =>
        {
            entity.Property(x => x.MobileNumber).HasMaxLength(11).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(6).IsRequired();
            entity.HasIndex(x => new { x.ResponderId, x.MobileNumber, x.IsUsed });
            entity.HasIndex(x => x.ExpiresAtUtc);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SecuritySettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MaxRequestsPerMinutePerIp).HasDefaultValue(20);
            entity.Property(x => x.MaxFailedOtpAttempts).HasDefaultValue(5);
            entity.Property(x => x.LockoutMinutes).HasDefaultValue(15);
            entity.Property(x => x.MaskAuthErrors).HasDefaultValue(true);
            entity.Property(x => x.EnableRateLimiting).HasDefaultValue(true);
            entity.Property(x => x.AnonymousLinkExpiryDays).HasDefaultValue(7);
            entity.Property(x => x.DispatchLinkRequireOtp).HasDefaultValue(false);
            entity.Property(x => x.AccessTokenLifetimeMinutes).HasDefaultValue(180);
            entity.Property(x => x.RefreshTokenLifetimeDays).HasDefaultValue(7);
        });

        builder.Entity<LoginAttempt>(entity =>
        {
            entity.Property(x => x.MobileNumber).HasMaxLength(11).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(45).IsRequired();
            entity.Property(x => x.AttemptType).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.IpAddress, x.AttemptType, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.MobileNumber, x.AttemptType, x.IsSuccess, x.CreatedAtUtc });
        });

        builder.Entity<SmsSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OtpEnabled).HasDefaultValue(true);
            entity.Property(x => x.SurveySendEnabled).HasDefaultValue(true);
            entity.Property(x => x.SurveyCompletedNotificationEnabled).HasDefaultValue(true);
            entity.Property(x => x.UserCreateSmsEnabled).HasDefaultValue(true);
            entity.Property(x => x.PublicFormRequireOtp).HasDefaultValue(false);
        });

        builder.Entity<SiteSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.PublicBaseUrl).HasMaxLength(500);
            entity.Property(x => x.AdminPanelBaseUrl).HasMaxLength(500);
        });

        builder.Entity<InboxMessage>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.AttachmentFileName).HasMaxLength(260);
            entity.Property(x => x.AttachmentPath).HasMaxLength(500);
            entity.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.UserId, x.IsArchived, x.IsRead, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.SenderUserId, x.CreatedAtUtc });
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Responder>(entity =>
        {
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.MobileNumber).HasMaxLength(11).IsRequired();
            entity.Property(x => x.NationalCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.HasIndex(x => x.MobileNumber).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => x.NationalCode).IsUnique().HasFilter("[IsDeleted] = 0 AND [NationalCode] <> ''");
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasIndex(x => x.CreatedAtUtc);
        });

        builder.Entity<ResponderGroup>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => x.CreatedAtUtc);
        });

        builder.Entity<ResponderGroupMember>(entity =>
        {
            entity.HasKey(x => new { x.ResponderId, x.GroupId });
            entity.HasOne(x => x.Responder)
                .WithMany(x => x.GroupMembers)
                .HasForeignKey(x => x.ResponderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Group)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.GroupId);
        });

        builder.Entity<UserGroup>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => x.CreatedAtUtc);
        });

        builder.Entity<UserGroupMember>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.GroupId });
            entity.HasOne(x => x.User)
                .WithMany(x => x.GroupMembers)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Group)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.GroupId);
        });

        builder.Entity<FormFieldGroupTemplate>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.FieldsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.UpdatedAtUtc);
        });

        builder.Entity<FormWordTemplate>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DocxFileName).HasMaxLength(260);
            entity.Property(x => x.DocxFilePath).HasMaxLength(500);
            entity.Property(x => x.DetectedPlaceholdersJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.FieldMappingsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.SignaturePlaceholderKey).HasMaxLength(120);
            entity.Property(x => x.SignatureImagePath).HasMaxLength(500);
            entity.Property(x => x.StampPlaceholderKey).HasMaxLength(120);
            entity.Property(x => x.StampImagePath).HasMaxLength(500);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.HasIndex(x => x.FormId).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasOne(x => x.Form).WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FormSubmissionWordDocument>(entity =>
        {
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.SubmissionId);
            entity.HasIndex(x => new { x.SubmissionId, x.TemplateId });
            entity.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FormWordBatchExportJob>(entity =>
        {
            entity.Property(x => x.SubmissionIdsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.ImageOverridesJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.ZipFilePath).HasMaxLength(500);
            entity.Property(x => x.ZipFileName).HasMaxLength(260);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2000);
            entity.Property(x => x.HangfireJobId).HasMaxLength(64);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasIndex(x => x.CreatedAtUtc);
        });

        builder.Entity<Form>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.ExpiresAtUtc);
            entity.Property(x => x.UserId).HasMaxLength(36);
            entity.Property(x => x.QuestionDisplayMode).HasMaxLength(20).HasDefaultValue("all");
            entity.Property(x => x.ApprovalEnabled).HasDefaultValue(false);
            entity.Property(x => x.ApprovalWorkflowJson).HasMaxLength(20000);
            entity.Property(x => x.WorkflowName).HasMaxLength(200);
            entity.HasOne(x => x.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(x => x.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.UserId, x.IsDeleted });
            entity.HasIndex(x => x.WorkflowTemplateId)
                .HasFilter("[WorkflowTemplateId] IS NOT NULL");
        });

        builder.Entity<FormWorkflowTemplate>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StepsJson).HasMaxLength(20000);
            entity.Property(x => x.ActionDirectionKey).HasMaxLength(80);
            entity.Property(x => x.ActionDirectionLabel).HasMaxLength(200);
            entity.Property(x => x.ActionAssigneeUserIdsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.CanvasLayoutJson).HasMaxLength(500);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.CreatedAtUtc });
        });

        builder.Entity<FormField>(entity =>
        {
            entity.Property(x => x.Label).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Placeholder).HasMaxLength(500);
            entity.Property(x => x.HelpText).HasMaxLength(1000);
            entity.Property(x => x.OptionsJson).HasMaxLength(5000);
            entity.Property(x => x.RowId).HasMaxLength(50);
            entity.Property(x => x.ColIndex).HasDefaultValue(0);
            entity.Property(x => x.RowColCount).HasDefaultValue(1);
            entity.Property(x => x.ConditionsJson).HasMaxLength(10000);
            entity.Property(x => x.UploadMaxSizeMb);
            entity.Property(x => x.DefaultValue).HasMaxLength(2000);
            entity.HasOne(x => x.Form)
                .WithMany(x => x.Fields)
                .HasForeignKey(x => x.FormId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.FormId, x.SortOrder });
        });

        builder.Entity<FormSubmission>(entity =>
        {
            entity.Property(x => x.SubmitterName).HasMaxLength(200);
            entity.Property(x => x.SubmitterEmail).HasMaxLength(300);
            entity.Property(x => x.TrackingCode).HasMaxLength(32);
            entity.HasIndex(x => x.TrackingCode).IsUnique().HasFilter("[TrackingCode] IS NOT NULL");
            entity.HasIndex(x => x.ResponderId);
            entity.HasIndex(x => x.DispatchLinkId);
            entity.Property(x => x.FieldsJson).HasMaxLength(20000);
            entity.Property(x => x.StepsJson).HasMaxLength(20000);
            entity.Property(x => x.WorkflowName).HasMaxLength(200);
            entity.Property(x => x.PostApprovalJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.Status).HasConversion<int>();
            entity.HasOne(x => x.Form)
                .WithMany(x => x.Submissions)
                .HasForeignKey(x => x.FormId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(x => x.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.FormId, x.SubmittedAtUtc });
            entity.HasIndex(x => x.WorkflowTemplateId)
                .HasFilter("[WorkflowTemplateId] IS NOT NULL");
            entity.HasIndex(x => new { x.Status, x.CurrentStepOrder });
        });

        builder.Entity<FormSubmissionApprovalLink>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.HasOne(x => x.FormSubmission)
                .WithMany()
                .HasForeignKey(x => x.FormSubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.FormSubmissionId, x.AssigneeUserId, x.IsActive });
        });

        builder.Entity<FormActionLink>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.FormSubmissionId, x.AssigneeUserId, x.IsActive });
            entity.HasOne(x => x.FormSubmission)
                .WithMany()
                .HasForeignKey(x => x.FormSubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FormDispatchLink>(entity =>
        {
            entity.Property(x => x.ResponderFullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ResponderMobileNumber).HasMaxLength(11).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.FormId, x.ResponderId, x.ExpiresAtUtc });
            entity.HasIndex(x => x.WorkflowTemplateId);
        });

        builder.Entity<FormUserAccess>(entity =>
        {
            entity.HasKey(x => new { x.FormId, x.UserId });
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(x => x.Form)
                .WithMany()
                .HasForeignKey(x => x.FormId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.UserId);
        });

        builder.Entity<ContractType>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        builder.Entity<ContractWorkflowTemplate>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StepsJson).HasMaxLength(20000);
            entity.Property(x => x.ActionDirectionKey).HasMaxLength(80);
            entity.Property(x => x.ActionDirectionLabel).HasMaxLength(200);
            entity.Property(x => x.ActionAssigneeUserIdsJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.CreatedAtUtc });
        });

        builder.Entity<ContractSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ApprovalWorkflowJson).HasMaxLength(20000);
            entity.Property(x => x.DocumentNumberPrefix).HasMaxLength(20).HasDefaultValue("EN");
            entity.HasData(new ContractSettings
            {
                Id = 1,
                ApprovalEnabled = false,
                DocumentNumberPrefix = "EN",
                DocumentSequencePeriod = 0,
                LastDocumentSequence = 0
            });
        });

        builder.Entity<ContractApprovalLink>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.ContractId, x.AssigneeUserId, x.IsActive });
            entity.HasOne(x => x.Contract)
                .WithMany()
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ContractActionLink>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.ContractId, x.AssigneeUserId, x.IsActive });
            entity.HasOne(x => x.Contract)
                .WithMany()
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ContractVersion>(entity =>
        {
            entity.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.PdfFilePath).HasMaxLength(500);
            entity.Property(x => x.CreatedByName).HasMaxLength(200);
            entity.Property(x => x.ChangeNote).HasMaxLength(500);
            entity.HasOne(x => x.Contract)
                .WithMany(c => c.Versions)
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ContractId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => x.CreatedAtUtc);
        });

        builder.Entity<ContractDocumentTemplate>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => new { x.IsActive, x.CreatedAtUtc });
            // NO ACTION: جلوگیری از multiple cascade paths در SQL Server (Template→Versions=CASCADE)
            entity.HasOne(x => x.ActiveVersion)
                .WithMany()
                .HasForeignKey(x => x.ActiveVersionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<ContractDocumentTemplateVersion>(entity =>
        {
            entity.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.DetectedPlaceholdersJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.ChangeNote).HasMaxLength(500);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.HasOne(x => x.Template)
                .WithMany(t => t.Versions)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.TemplateId, x.VersionNumber }).IsUnique();
        });

        builder.Entity<ContractDocumentTemplateField>(entity =>
        {
            entity.Property(x => x.Key).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DesignerOrderJson).HasMaxLength(500);
            entity.Property(x => x.DefaultValue).HasMaxLength(500);
            entity.Property(x => x.OptionsJson).HasMaxLength(2000);
            entity.Property(x => x.FieldType).HasConversion<int>();
            entity.HasOne(x => x.Template)
                .WithMany(t => t.Fields)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Version)
                .WithMany(v => v.Fields)
                .HasForeignKey(x => x.VersionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.VersionId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.VersionId, x.SortOrder });
        });

        builder.Entity<Contract>(entity =>
        {
            entity.Property(x => x.ContractNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.NationalId).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(11).IsRequired();
            entity.Property(x => x.SubjectPersonName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FilePath).HasMaxLength(500);
            entity.Property(x => x.OriginalFilePath).HasMaxLength(500);
            entity.Property(x => x.PdfFilePath).HasMaxLength(500);
            entity.Property(x => x.FileName).HasMaxLength(260);
            entity.Property(x => x.CreatedByName).HasMaxLength(200);
            entity.Property(x => x.WorkflowName).HasMaxLength(200);
            entity.Property(x => x.StepsJson).HasMaxLength(20000);
            entity.Property(x => x.PostApprovalJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.TemplateFieldValuesJson).HasMaxLength(20000);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.IndexStatus).HasConversion<int>();
            entity.HasOne(x => x.TextIndex)
                .WithOne(x => x.Contract)
                .HasForeignKey<ContractTextIndex>(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.WorkflowTemplateId);
            entity.HasIndex(x => x.ContractDocumentTemplateId);
            entity.HasOne(x => x.ContractDocumentTemplate)
                .WithMany()
                .HasForeignKey(x => x.ContractDocumentTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ContractType)
                .WithMany()
                .HasForeignKey(x => x.ContractTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(x => x.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.ContractNumber).IsUnique();
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => new { x.Status, x.CurrentStepOrder });
            entity.HasIndex(x => x.IsArchived);
            entity.Property(x => x.IsSoftDeleted).HasDefaultValue(false);
            entity.HasIndex(x => x.IsSoftDeleted);
            entity.HasQueryFilter(x => !x.IsSoftDeleted);
            entity.HasIndex(x => x.Title);
            entity.HasIndex(x => new { x.Status, x.IsArchived })
                .HasFilter("[Status] = 3 AND [IsArchived] = 0 AND [PostApprovalJson] IS NOT NULL");
            entity.HasIndex(x => new { x.CreatedByUserId, x.Status });
        });

        builder.Entity<ContractTextIndex>(entity =>
        {
            entity.HasKey(x => x.ContractId);
            entity.Property(x => x.ExtractedText).HasColumnType("nvarchar(max)");
            entity.Property(x => x.NormalizedText).HasColumnType("nvarchar(max)");
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.Property(x => x.ExtractorVersion).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.ExtractedAt);
        });

        builder.Entity<DocumentFolder>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ParentId, x.Name, x.IsDeleted })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => new { x.ParentId, x.CreatedAtUtc });
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<Document>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ReferenceNumber).HasMaxLength(80);
            entity.Property(x => x.ManualReferenceNumber).HasMaxLength(80);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.AccessLevel).HasConversion<int>();
            entity.HasOne(x => x.Folder)
                .WithMany()
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.FolderId, x.IsDeleted, x.UpdatedAtUtc });
            entity.HasIndex(x => new { x.OwnerUserId, x.UpdatedAtUtc });
            entity.HasIndex(x => new { x.Category, x.AccessLevel, x.UpdatedAtUtc });
            entity.HasIndex(x => x.OrganizationalUnitId);
            entity.HasIndex(x => x.ProjectId);
            entity.HasOne(x => x.OrganizationalUnit)
                .WithMany()
                .HasForeignKey(x => x.OrganizationalUnitId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.WorkflowName).HasMaxLength(200);
            entity.Property(x => x.StepsJson).HasMaxLength(20000);
            entity.Property(x => x.PostApprovalJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.WorkflowStatus).HasConversion<int>();
            entity.HasOne(x => x.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(x => x.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.WorkflowTemplateId)
                .HasFilter("[WorkflowTemplateId] IS NOT NULL");
            entity.HasIndex(x => new { x.WorkflowStatus, x.CurrentStepOrder });
            entity.Property(x => x.LegalHoldReason).HasMaxLength(500);
            entity.Property(x => x.ObsoleteReason).HasMaxLength(500);
            entity.Property(x => x.ArchiveTier).HasConversion<int>();
            entity.Property(x => x.LifecycleStatus).HasConversion<int>();
            entity.HasOne(x => x.RetentionPolicy)
                .WithMany()
                .HasForeignKey(x => x.RetentionPolicyId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.IsArchived);
            entity.HasIndex(x => new { x.LifecycleStatus, x.IsArchived, x.UpdatedAtUtc });
            entity.HasIndex(x => x.LegalHold).HasFilter("[LegalHold] = 1");
            entity.HasIndex(x => new { x.ScheduledArchiveAtUtc, x.IsArchived })
                .HasFilter("[ScheduledArchiveAtUtc] IS NOT NULL AND [IsArchived] = 0");
            entity.HasIndex(x => new { x.ScheduledDeleteAtUtc, x.LegalHold, x.LongTermRetention })
                .HasFilter("[ScheduledDeleteAtUtc] IS NOT NULL AND [LegalHold] = 0 AND [LongTermRetention] = 0");
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        builder.Entity<DocumentRetentionPolicy>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.CategoryMatch).HasMaxLength(120);
            entity.Property(x => x.AccessLevelMatch).HasConversion<int?>();
            entity.HasIndex(x => new { x.IsActive, x.IsDefault, x.SortOrder });
            entity.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<DocumentLifecycleSettings>(entity =>
        {
            entity.HasOne(x => x.DefaultRetentionPolicy)
                .WithMany()
                .HasForeignKey(x => x.DefaultRetentionPolicyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DocumentWorkflowTemplate>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StepsJson).HasMaxLength(20000);
            entity.Property(x => x.ActionDirectionKey).HasMaxLength(80);
            entity.Property(x => x.ActionDirectionLabel).HasMaxLength(200);
            entity.Property(x => x.ActionAssigneeUserIdsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.CanvasLayoutJson).HasMaxLength(500);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.CreatedAtUtc });
        });

        builder.Entity<DocumentApprovalLink>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.DocumentId, x.AssigneeUserId, x.IsActive });
        });

        builder.Entity<DocumentSystemTag>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasIndex(x => new { x.IsActive, x.CreatedAtUtc });
        });

        builder.Entity<DocumentSystemCategory>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasIndex(x => new { x.IsActive, x.CreatedAtUtc });
        });

        builder.Entity<DocumentSystemOrganizationalUnit>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasIndex(x => new { x.IsActive, x.CreatedAtUtc });
        });

        builder.Entity<DocumentSystemProject>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasIndex(x => new { x.IsActive, x.CreatedAtUtc });
        });

        builder.Entity<DocumentVersion>(entity =>
        {
            entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.StoredPath).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Extension).HasMaxLength(16).IsRequired();
            entity.Property(x => x.ContentHashSha256).HasMaxLength(64);
            entity.Property(x => x.ChangeLog).HasMaxLength(500);
            entity.Property(x => x.EncryptionKeyId).HasMaxLength(32);
            entity.Property(x => x.FileNonceBase64).HasMaxLength(32);
            entity.Property(x => x.EncryptedDekBase64).HasMaxLength(256);
            entity.HasIndex(x => new { x.IsEncrypted, x.EncryptionKeyId });
            entity.HasOne(x => x.Document)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => new { x.DocumentId, x.UploadedAtUtc });
        });

        builder.Entity<DocumentVersionText>(entity =>
        {
            entity.HasKey(x => x.DocumentVersionId);
            entity.Property(x => x.ExtractedText).HasColumnType("nvarchar(max)");
            entity.Property(x => x.NormalizedText).HasColumnType("nvarchar(max)");
            entity.Property(x => x.ProcessingStatus).HasConversion<int>();
            entity.Property(x => x.ErrorMessage).HasMaxLength(2000);
            entity.HasOne(x => x.Version)
                .WithOne(x => x.TextIndex)
                .HasForeignKey<DocumentVersionText>(x => x.DocumentVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.DocumentId);
            entity.HasIndex(x => new { x.ProcessingStatus, x.UpdatedAtUtc });
        });

        builder.Entity<DocumentTag>(entity =>
        {
            entity.HasKey(x => new { x.DocumentId, x.Tag });
            entity.Property(x => x.Tag).HasMaxLength(80).IsRequired();
            entity.HasOne(x => x.Document)
                .WithMany(x => x.Tags)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.Tag);
        });

        builder.Entity<DocumentActivity>(entity =>
        {
            entity.Property(x => x.EventType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(45);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.OldValuesJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.NewValuesJson).HasColumnType("nvarchar(max)");
            entity.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.DocumentId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.EventType, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.ActorUserId, x.CreatedAtUtc });
        });

        builder.Entity<DocumentPermissionConfig>(entity =>
        {
            entity.HasKey(x => new { x.ResourceType, x.ResourceId });
            entity.Property(x => x.ResourceType).HasConversion<int>();
            entity.HasIndex(x => new { x.ResourceType, x.ResourceId }).IsUnique();
            entity.HasIndex(x => x.UpdatedAtUtc);
        });

        builder.Entity<DocumentPermissionEntry>(entity =>
        {
            entity.Property(x => x.ResourceType).HasConversion<int>();
            entity.Property(x => x.SubjectType).HasConversion<int>();
            entity.Property(x => x.Level).HasConversion<int>();
            entity.HasIndex(x => new { x.ResourceType, x.ResourceId, x.SubjectType, x.SubjectId })
                .IsUnique();
            entity.HasIndex(x => new { x.ResourceType, x.ResourceId, x.Level });
            entity.HasIndex(x => new { x.SubjectType, x.SubjectId });
        });

        builder.Entity<DocumentShareLink>(entity =>
        {
            entity.Property(x => x.ResourceType).HasConversion<int>();
            entity.Property(x => x.Scope).HasConversion<int>();
            entity.Property(x => x.Token).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SpecificSubjectIdsJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(x => x.Token).IsUnique();
            entity.HasIndex(x => new { x.ResourceType, x.ResourceId, x.IsRevoked, x.CreatedAtUtc });
        });

        builder.Entity<DocumentPublicProfile>(entity =>
        {
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.PublicDescription).HasMaxLength(4000);
            entity.Property(x => x.DocumentType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CoverImagePath).HasMaxLength(500);
            entity.Property(x => x.Language).HasMaxLength(10).IsRequired();
            entity.Property(x => x.SeoTitle).HasMaxLength(200);
            entity.Property(x => x.SeoDescription).HasMaxLength(500);
            entity.Property(x => x.SeoKeywords).HasMaxLength(300);
            entity.Property(x => x.PublicVisibilityStatus).HasConversion<int>();
            entity.HasOne(x => x.Document)
                .WithOne()
                .HasForeignKey<DocumentPublicProfile>(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PublicCategory)
                .WithMany()
                .HasForeignKey(x => x.PublicCategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.PublicCollection)
                .WithMany()
                .HasForeignKey(x => x.PublicCollectionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.PublicVersion)
                .WithMany()
                .HasForeignKey(x => x.PublicVersionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.PublicVisibilityStatus, x.PublishedAtUtc });
            entity.HasIndex(x => new { x.PublicCategoryId, x.Featured, x.Pinned });
            entity.HasIndex(x => new { x.PublicCollectionId, x.PublishedAtUtc });
            entity.HasIndex(x => x.Language);
            entity.HasIndex(x => new { x.PublishStartAtUtc, x.PublishEndAtUtc });
        });

        builder.Entity<PublicCategory>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.CoverImagePath).HasMaxLength(500);
            entity.Property(x => x.Icon).HasMaxLength(80);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        builder.Entity<PublicCollection>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.CoverImagePath).HasMaxLength(500);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.Featured, x.SortOrder });
        });

        builder.Entity<PublicBanner>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Subtitle).HasMaxLength(400);
            entity.Property(x => x.ImagePath).HasMaxLength(500);
            entity.Property(x => x.CtaLabel).HasMaxLength(80);
            entity.Property(x => x.CtaUrl).HasMaxLength(500);
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        builder.Entity<PublicPortalSettings>(entity =>
        {
            entity.Property(x => x.SiteTitle).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LogoPath).HasMaxLength(500);
            entity.Property(x => x.PrimaryColor).HasMaxLength(20);
            entity.Property(x => x.SecondaryColor).HasMaxLength(20);
            entity.Property(x => x.AboutText).HasMaxLength(2000);
            entity.Property(x => x.ContactEmail).HasMaxLength(200);
            entity.Property(x => x.ContactPhone).HasMaxLength(40);
            entity.Property(x => x.FooterLinksJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.SocialLinksJson).HasColumnType("nvarchar(max)");
        });

        builder.Entity<PublicAnalyticsEvent>(entity =>
        {
            entity.Property(x => x.EventType).HasConversion<int>();
            entity.Property(x => x.VisitorKey).HasMaxLength(64);
            entity.Property(x => x.IpHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.DocumentId, x.EventType, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.VisitorKey, x.DocumentId, x.EventType, x.CreatedAtUtc });
        });

        // ── Static seed data (captured in migrations via HasData) ────────────────
        var adminRoleId  = new Guid("10000000-0000-0000-0000-000000000001");
        var expertRoleId = new Guid("10000000-0000-0000-0000-000000000002");

        builder.Entity<AppRole>().HasData(
            new AppRole { Id = adminRoleId,  Name = "Admin",  NormalizedName = "ADMIN",  DisplayName = "مدیر سیستم", ConcurrencyStamp = "00000000-seed-0000-0000-000000000001" },
            new AppRole { Id = expertRoleId, Name = "Expert", NormalizedName = "EXPERT", DisplayName = "کارشناس",    ConcurrencyStamp = "00000000-seed-0000-0000-000000000002" }
        );

        var permUsersRead     = new Guid("20000000-0000-0000-0000-000000000001");
        var permUsersAdd      = new Guid("20000000-0000-0000-0000-000000000002");
        var permUsersUpdate   = new Guid("20000000-0000-0000-0000-000000000003");
        var permUsersDelete   = new Guid("20000000-0000-0000-0000-000000000004");
        var permSettingsRead  = new Guid("20000000-0000-0000-0000-000000000005");
        var permSettingsUpdate= new Guid("20000000-0000-0000-0000-000000000006");
        var permRolesRead     = new Guid("20000000-0000-0000-0000-000000000007");
        var permRolesUpdate   = new Guid("20000000-0000-0000-0000-000000000008");
        var permMenusView     = new Guid("20000000-0000-0000-0000-000000000009");
        var permProfileUpdate = new Guid("20000000-0000-0000-0000-000000000010");
        var permMessagesRead  = new Guid("20000000-0000-0000-0000-000000000011");
        var permFormsRead     = new Guid("20000000-0000-0000-0000-000000000012");
        var permFormsAdd      = new Guid("20000000-0000-0000-0000-000000000013");
        var permFormsUpdate   = new Guid("20000000-0000-0000-0000-000000000014");
        var permFormsDelete   = new Guid("20000000-0000-0000-0000-000000000015");

        builder.Entity<Permission>().HasData(
            new Permission { Id = permUsersRead,      Name = "users.read"      },
            new Permission { Id = permUsersAdd,       Name = "users.add"       },
            new Permission { Id = permUsersUpdate,    Name = "users.update"    },
            new Permission { Id = permUsersDelete,    Name = "users.delete"    },
            new Permission { Id = permSettingsRead,   Name = "settings.read"   },
            new Permission { Id = permSettingsUpdate, Name = "settings.update" },
            new Permission { Id = permRolesRead,      Name = "roles.read"      },
            new Permission { Id = permRolesUpdate,    Name = "roles.update"    },
            new Permission { Id = permMenusView,      Name = "menus.view"      },
            new Permission { Id = permProfileUpdate,  Name = "profile.update"  },
            new Permission { Id = permMessagesRead,   Name = "messages.read"   },
            new Permission { Id = permFormsRead,      Name = "forms.read"      },
            new Permission { Id = permFormsAdd,       Name = "forms.add"       },
            new Permission { Id = permFormsUpdate,    Name = "forms.update"    },
            new Permission { Id = permFormsDelete,    Name = "forms.delete"    }
        );

        builder.Entity<RolePermission>().HasData(
            new RolePermission { RoleId = adminRoleId,  PermissionId = permUsersRead      },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permUsersAdd       },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permUsersUpdate    },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permUsersDelete    },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permSettingsRead   },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permSettingsUpdate },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permRolesRead      },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permRolesUpdate    },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permMenusView      },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permProfileUpdate  },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permMessagesRead   },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permFormsRead      },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permFormsAdd       },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permFormsUpdate    },
            new RolePermission { RoleId = adminRoleId,  PermissionId = permFormsDelete    },
            new RolePermission { RoleId = expertRoleId, PermissionId = permMenusView      },
            new RolePermission { RoleId = expertRoleId, PermissionId = permProfileUpdate  },
            new RolePermission { RoleId = expertRoleId, PermissionId = permMessagesRead   },
            new RolePermission { RoleId = expertRoleId, PermissionId = permFormsRead      },
            new RolePermission { RoleId = expertRoleId, PermissionId = permFormsAdd       },
            new RolePermission { RoleId = expertRoleId, PermissionId = permFormsUpdate    }
        );

        var menuForms    = new Guid("30000000-0000-0000-0000-000000000006");
        var menuUsers    = new Guid("30000000-0000-0000-0000-000000000001");
        var menuSettings = new Guid("30000000-0000-0000-0000-000000000002");
        var menuSms      = new Guid("30000000-0000-0000-0000-000000000003");
        var menuSecurity = new Guid("30000000-0000-0000-0000-000000000004");
        var menuAccess   = new Guid("30000000-0000-0000-0000-000000000005");

        builder.Entity<MenuItem>().HasData(
            new MenuItem { Id = menuForms,    Key = "forms",             Title = "فرم ساز",          Icon = "LayoutTemplate", IconColor = "#10B981", Route = "/admin/forms",              Order = 0 },
            new MenuItem { Id = menuUsers,    Key = "users",             Title = "مدیریت کاربران",   Icon = "Users",          IconColor = "#0EA5E9", Route = "/admin/users",              Order = 1 },
            new MenuItem { Id = menuSettings, Key = "settings",          Title = "تنظیمات سایت",     Icon = "Settings",       IconColor = "#F59E0B",                                      Order = 2 },
            new MenuItem { Id = menuSms,      Key = "settings.sms",      Title = "تنظیمات پیامک",    Icon = "MessageSquare",  IconColor = "#8B5CF6", Route = "/admin/settings/sms",      Order = 1, ParentId = menuSettings },
            new MenuItem { Id = menuSecurity, Key = "settings.security", Title = "تنظیمات امنیتی",   Icon = "ShieldCheck",    IconColor = "#EF4444", Route = "/admin/settings/security", Order = 2, ParentId = menuSettings },
            new MenuItem { Id = menuAccess,   Key = "settings.access",   Title = "سطح دسترسی",       Icon = "Shield",         IconColor = "#2563EB", Route = "/admin/access-level",      Order = 3, ParentId = menuSettings }
        );

        builder.Entity<RoleMenu>().HasData(
            new RoleMenu { RoleId = adminRoleId,  MenuId = menuForms    },
            new RoleMenu { RoleId = adminRoleId,  MenuId = menuUsers    },
            new RoleMenu { RoleId = adminRoleId,  MenuId = menuSettings },
            new RoleMenu { RoleId = adminRoleId,  MenuId = menuSms      },
            new RoleMenu { RoleId = adminRoleId,  MenuId = menuSecurity },
            new RoleMenu { RoleId = adminRoleId,  MenuId = menuAccess   },
            new RoleMenu { RoleId = expertRoleId, MenuId = menuForms    },
            new RoleMenu { RoleId = expertRoleId, MenuId = menuUsers    }
        );

        builder.Entity<SecuritySettings>().HasData(new SecuritySettings
        {
            Id = 1,
            EnableRateLimiting = true, MaxRequestsPerMinutePerIp = 20,
            MaxFailedOtpAttempts = 5, LockoutMinutes = 15, MaskAuthErrors = true,
            LoginMethod = LoginMethod.OtpOnly,
            AnonymousLinkExpiryDays = 7,
            DispatchLinkRequireOtp = false,
            AccessTokenLifetimeMinutes = 180,
            RefreshTokenLifetimeDays = 7
        });

        builder.Entity<SmsSettings>().HasData(new SmsSettings
        {
            Id = 1,
            OtpEnabled = true,
            SurveySendEnabled = true,
            SurveyCompletedNotificationEnabled = true,
            FormSubmissionTrackingSmsEnabled = true,
            UserCreateSmsEnabled = true,
            ApprovalReferralSmsEnabled = true,
            FormWorkflowCompletedSenderSmsEnabled = true,
            FormActionPhaseCompletedSenderSmsEnabled = true,
            FormResponderApprovedSmsEnabled = true,
            FormWorkflowRejectedSenderSmsEnabled = true,
            FormWorkflowRejectedResponderSmsEnabled = true,
            ContractCreatorApprovalNotifySmsEnabled = true,
            ContractAmendmentAssigneeSmsEnabled = true,
            ContractAmendmentReturnToRejecterSmsEnabled = true,
            ContractRejectionNotifySmsEnabled = true,
            ContractActionCompletedCreatorSmsEnabled = true,
            ApprovalReminderSmsEnabled = false,
            ApprovalReminderDelayDays = 0,
            ApprovalReminderDelayHours = 24,
            WorkflowValidityReminderSmsEnabled = false,
            WorkflowValiditySuspensionDelayDays = 0,
            WorkflowValiditySuspensionDelayHours = 24,
            PublicFormRequireOtp = false,
        });
    }
}
