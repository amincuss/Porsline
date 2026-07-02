using System.Text;
using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure;
using PorslineClone.Infrastructure.Auth;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Api.HangfireJobs;
using PorslineClone.Api.Middleware;
using PorslineClone.Api.RuleEngine;
using PorslineClone.Application.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", p =>
        p.AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true));
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// یکسان‌سازی فرمت خطاهای validation - همیشه { message } برگردانده می‌شود
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors.Select(x =>
                string.IsNullOrEmpty(x.ErrorMessage) ? $"فیلد {e.Key} نامعتبر است" : x.ErrorMessage))
            .ToList();
        var message = errors.Count > 0 ? string.Join(" | ", errors) : "ورودی نامعتبر است";
        return new BadRequestObjectResult(new { message });
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    // جلوگیری از تداخل schema برای انواع هم‌نام در لایه‌های مختلف
    options.CustomSchemaIds(type =>
    {
        var id = type.FullName?.Replace("+", ".");
        if (!string.IsNullOrEmpty(id))
            return id;
        return $"{type.Namespace}.{type.Name}";
    });
});
builder.Services.AddMemoryCache();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IRuleEvaluationService, RuleEvaluationService>();

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(Hangfire.CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<IContractIndexEnqueue, HangfireContractIndexEnqueue>();
builder.Services.AddScoped<IFormWordBatchExportEnqueue, HangfireFormWordBatchExportEnqueue>();
builder.Services.AddScoped<IFormSubmissionExcelExportEnqueue, HangfireFormSubmissionExcelExportEnqueue>();
builder.Services.AddScoped<IFormDispatchGroupSendEnqueue, HangfireFormDispatchGroupSendEnqueue>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var key = Encoding.UTF8.GetBytes(jwt.Key);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("users.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.read") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("users.add", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.add") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("users.import", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.import") || ctx.User.HasClaim("permission", "users.add") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("users.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.update") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("users.delete", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.delete") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("users.access.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.access.read") || ctx.User.HasClaim("permission", "users.update") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("users.access.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.access.update") || ctx.User.HasClaim("permission", "users.update") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("settings.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "settings.read") || ctx.User.HasClaim("permission", "settings.crud")));
    options.AddPolicy("settings.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "settings.update") || ctx.User.HasClaim("permission", "settings.crud")));
    options.AddPolicy("settings.sms.logs.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "settings.sms.logs.read")
        || ctx.User.HasClaim("permission", "settings.read")
        || ctx.User.HasClaim("permission", "settings.crud")));
    options.AddPolicy("settings.sms.test", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "settings.sms.test")
        || ctx.User.HasClaim("permission", "settings.update")
        || ctx.User.HasClaim("permission", "settings.crud")));
    options.AddPolicy("roles.read", p => p.RequireClaim("permission", "roles.read"));
    options.AddPolicy("roles.update", p => p.RequireClaim("permission", "roles.update"));
    options.AddPolicy("forms.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.read") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("forms.add", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.add") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("forms.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.update") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("forms.delete", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.delete") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("exams.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "exams.read") || ctx.User.HasClaim("permission", "forms.read") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("exams.add", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "exams.add") || ctx.User.HasClaim("permission", "forms.add") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("exams.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "exams.update") || ctx.User.HasClaim("permission", "forms.update") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("exams.delete", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "exams.delete") || ctx.User.HasClaim("permission", "forms.delete") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("forms.rules.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.rules.read") || ctx.User.HasClaim("permission", "forms.read") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("forms.rules.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.rules.update") || ctx.User.HasClaim("permission", "forms.update") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("forms.access.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.access.read") || ctx.User.HasClaim("permission", "forms.update") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("forms.access.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.access.update") || ctx.User.HasClaim("permission", "forms.update") || ctx.User.HasClaim("permission", "forms.crud")));
    options.AddPolicy("approvals.read", p => p.RequireClaim("permission", "approvals.read"));
    options.AddPolicy("approvals.update", p => p.RequireClaim("permission", "approvals.update"));
    options.AddPolicy("forms.archive.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "forms.archive.read")
        || ctx.User.HasClaim("permission", "forms.archive.read.all")));
    options.AddPolicy("workflow-runs.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "workflow-runs.read")
        || ctx.User.HasClaim("permission", "workflow-runs.read.all")));
    options.AddPolicy("workflow-runs.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "workflow-runs.update")
        || ctx.User.HasClaim("permission", "approvals.update")));
    options.AddPolicy("actions.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "actions.read") || ctx.User.HasClaim("permission", "actions.read.all")));
    options.AddPolicy("actions.update", p => p.RequireClaim("permission", "actions.update"));
    options.AddPolicy("responders.read", p => p.RequireClaim("permission", "responders.read"));
    options.AddPolicy("responders.add", p => p.RequireClaim("permission", "responders.add"));
    options.AddPolicy("responders.update", p => p.RequireClaim("permission", "responders.update"));
    options.AddPolicy("responders.delete", p => p.RequireClaim("permission", "responders.delete"));
    options.AddPolicy("responders.userforms.delete", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "responders.userforms.delete")
        || ctx.User.HasClaim("permission", "responders.delete")));
    options.AddPolicy("responders.userforms.workflow", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "responders.userforms.workflow")
        || ctx.User.HasClaim("permission", "forms.update")));
    options.AddPolicy("responders.userforms.workflow.restart", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "responders.userforms.workflow.restart")
        || ctx.User.HasClaim("permission", "responders.userforms.workflow")
        || ctx.User.HasClaim("permission", "forms.update")));
    options.AddPolicy("respondergroups.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "respondergroups.read") || ctx.User.HasClaim("permission", "responders.read")));
    options.AddPolicy("respondergroups.add", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "respondergroups.add") || ctx.User.HasClaim("permission", "responders.add")));
    options.AddPolicy("respondergroups.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "respondergroups.update") || ctx.User.HasClaim("permission", "responders.update")));
    options.AddPolicy("respondergroups.delete", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "respondergroups.delete") || ctx.User.HasClaim("permission", "responders.delete")));
    options.AddPolicy("responders.send", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "responders.send") || ctx.User.HasClaim("permission", "responders.update")));
    options.AddPolicy("responders.send.access", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "responders.send")
        || ctx.User.HasClaim("permission", "responders.send.activation")
        || ctx.User.HasClaim("permission", "responders.update")));
    options.AddPolicy("responders.send.activation", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "responders.send.activation") || ctx.User.HasClaim("permission", "responders.update")));
    options.AddPolicy("usergroups.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "usergroups.read") || ctx.User.HasClaim("permission", "usergroups.read.all")));
    options.AddPolicy("usergroups.add", p => p.RequireClaim("permission", "usergroups.add"));
    options.AddPolicy("usergroups.update", p => p.RequireClaim("permission", "usergroups.update"));
    options.AddPolicy("usergroups.delete", p => p.RequireClaim("permission", "usergroups.delete"));
    options.AddPolicy("contracts.read", p => p.RequireClaim("permission", "contracts.read"));
    options.AddPolicy("contracts.archive.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "contracts.archive.read")
        || ctx.User.HasClaim("permission", "contracts.archive.read.all")));
    options.AddPolicy("contracts.add", p => p.RequireClaim("permission", "contracts.add"));
    options.AddPolicy("contracts.update", p => p.RequireClaim("permission", "contracts.update"));
    options.AddPolicy("contracts.delete", p => p.RequireClaim("permission", "contracts.delete"));
    options.AddPolicy("contracts.settings.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "contracts.settings.read") || ctx.User.HasClaim("permission", "contracts.settings.update")));
    options.AddPolicy("contracts.settings.update", p => p.RequireClaim("permission", "contracts.settings.update"));
    options.AddPolicy("contracts.settings.delete", p => p.RequireClaim("permission", "contracts.settings.delete"));
    options.AddPolicy("forms.rules.delete", p => p.RequireClaim("permission", "forms.rules.delete"));
    options.AddPolicy("documents.workflow.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "documents.workflow.read") || ctx.User.HasClaim("permission", "forms.read")));
    options.AddPolicy("documents.workflow.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "documents.workflow.update") || ctx.User.HasClaim("permission", "forms.update")));
    options.AddPolicy("documents.workflow.delete", p => p.RequireClaim("permission", "documents.workflow.delete"));
    options.AddPolicy("documents.workflow.restart", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "documents.workflow.restart")
        || ctx.User.HasClaim("permission", "documents.workflow.update")
        || ctx.User.HasClaim("permission", "forms.update")));
    options.AddPolicy("settings.delete", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "settings.delete") || ctx.User.HasClaim("permission", "settings.crud")));
    options.AddPolicy("messages.read", p => p.RequireClaim("permission", "messages.read"));
    options.AddPolicy("messages.read.all", p => p.RequireClaim("permission", "messages.read.all"));
    options.AddPolicy("messages.send", p => p.RequireClaim("permission", "messages.send"));
    options.AddPolicy("messages.delete", p => p.RequireClaim("permission", "messages.delete"));
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("DevCors");

var pathBase = (builder.Configuration["Backend:PathBase"] ?? "").Trim().TrimEnd('/');
if (!string.IsNullOrEmpty(pathBase))
{
    if (!pathBase.StartsWith('/'))
        pathBase = "/" + pathBase;
    app.UsePathBase(pathBase);
}

app.UseApiExceptionHandling();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Formiq API v1");
    options.RoutePrefix = "swagger";
});

var profileImagesRoot = Path.Combine(app.Environment.ContentRootPath, "ProfileImages");
Directory.CreateDirectory(profileImagesRoot);
var formUploadRoot = Path.Combine(app.Environment.ContentRootPath, "Formupload");
Directory.CreateDirectory(formUploadRoot);
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "Documents"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(profileImagesRoot),
    RequestPath = "/ProfileImages",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        ctx.Context.Response.Headers.Pragma = "no-cache";
        ctx.Context.Response.Headers.Expires = "0";
        ctx.Context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAdminAuthorizationFilter()],
});
app.MapControllers();

var dbStartup = app.Configuration.GetSection(PorslineClone.Infrastructure.Options.DatabaseStartupOptions.SectionName)
    .Get<PorslineClone.Infrastructure.Options.DatabaseStartupOptions>()
    ?? new PorslineClone.Infrastructure.Options.DatabaseStartupOptions();

if (dbStartup.RunMigrations || dbStartup.RunSeed || dbStartup.ApplySchemaPatch)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");

    if (dbStartup.RunMigrations)
    {
        try
        {
            logger.LogInformation("Applying EF Core migrations...");
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "EF migration failed.");
            if (!dbStartup.ContinueOnMigrationError)
                throw;
        }
    }
    else
    {
        logger.LogInformation("RunMigrations is disabled. Apply migrations manually (dotnet ef database update).");
    }

    if (dbStartup.ApplySchemaPatch || dbStartup.RunMigrations)
    {
        try
        {
            await DatabaseSchemaPatcher.ApplySecuritySettingsColumnsAsync(db);
            await DatabaseSchemaPatcher.EnsureFormSubmissionSoftDeleteSchemaAsync(db);
            await DatabaseSchemaPatcher.EnsureFormFieldsRepeatableSchemaAsync(db);
            logger.LogInformation("SecuritySettings schema columns verified.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SecuritySettings schema patch failed.");
            if (!dbStartup.ContinueOnMigrationError)
                throw;
        }

        try
        {
            await DatabaseSchemaPatcher.EnsureDocumentWorkflowSchemaAsync(db, logger);
            await DatabaseSchemaPatcher.EnsureDocumentLifecycleSchemaAsync(db, logger);
            await DatabaseSchemaPatcher.EnsureDocumentEncryptionSchemaAsync(db, logger);
            await DatabaseSchemaPatcher.EnsureDocumentClassificationSchemaAsync(db, logger);
            await DatabaseSchemaPatcher.EnsurePublicDocumentPortalSchemaAsync(db, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Document lifecycle schema patch failed.");
            if (!dbStartup.ContinueOnMigrationError)
                throw;
        }
    }

    if (dbStartup.ApplySchemaPatch)
    {
        try
        {
            await DatabaseSchemaPatcher.ApplyAsync(db, logger);
            try
            {
                await scope.ServiceProvider.GetRequiredService<ISmsPatternService>().EnsureSeededAsync();
                await DbSeeder.EnsureSmsPatternsMenuAsync(db);
                await DbSeeder.EnsureSmsLogsMenuAsync(db);
                await DbSeeder.EnsureSmsTestMenuAsync(db);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SmsPattern seed skipped or failed.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database schema patch failed.");
            if (!dbStartup.ContinueOnMigrationError)
                throw;
        }
    }
    else
    {
        logger.LogInformation("ApplySchemaPatch is disabled. Apply schema changes manually.");
    }

    if (dbStartup.RunSeed)
    {
        try
        {
            logger.LogInformation("Running database seed...");
            await DbSeeder.EnsureReferenceDataAsync(db, roleManager);
            await DbSeeder.SeedAdminUserAsync(db, userManager);
            await PorslineClone.Infrastructure.Services.Documents.PublicPortalSeedService.SeedDemoContentAsync(db);
            logger.LogInformation("Database seed completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database seed failed.");
            if (!dbStartup.ContinueOnMigrationError)
                throw;
        }
    }
}

var userSignaturesRoot = Path.Combine(app.Environment.ContentRootPath, "UserSignatures");
Directory.CreateDirectory(userSignaturesRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(userSignaturesRoot),
    RequestPath = "/UserSignatures"
});

try
{
    using var schemaScope = app.Services.CreateScope();
    var schemaDb = schemaScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseSchemaPatcher.EnsureFormFieldsRepeatableSchemaAsync(schemaDb);
    await DatabaseSchemaPatcher.EnsureUserFormsSidebarSchemaAsync(schemaDb);
}
catch (Exception ex)
{
    var schemaLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
    schemaLogger.LogWarning(ex, "Critical form schema patch skipped or failed.");
}

app.Run();

sealed class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.IsInRole("Admin");
}
