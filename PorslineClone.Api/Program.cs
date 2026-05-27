using System.Text;
using System.Text.Json.Serialization;
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
using PorslineClone.Api.Middleware;
using PorslineClone.Api.RuleEngine;

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
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IRuleEvaluationService, RuleEvaluationService>();

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
    options.AddPolicy("users.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.update") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("users.delete", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "users.delete") || ctx.User.HasClaim("permission", "users.crud")));
    options.AddPolicy("settings.read", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "settings.read") || ctx.User.HasClaim("permission", "settings.crud")));
    options.AddPolicy("settings.update", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "settings.update") || ctx.User.HasClaim("permission", "settings.crud")));
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
    options.AddPolicy("responders.send", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "responders.send") || ctx.User.HasClaim("permission", "responders.update")));
    options.AddPolicy("usergroups.read", p => p.RequireClaim("permission", "usergroups.read"));
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
    options.AddPolicy("settings.delete", p => p.RequireClaim("permission", "settings.delete"));
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
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PorslineClone API v1");
    options.RoutePrefix = "swagger";
});

var profileImagesRoot = Path.Combine(app.Environment.ContentRootPath, "ProfileImages");
Directory.CreateDirectory(profileImagesRoot);
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
            logger.LogInformation("SecuritySettings schema columns verified.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SecuritySettings schema patch failed.");
            if (!dbStartup.ContinueOnMigrationError)
                throw;
        }
    }

    if (dbStartup.ApplySchemaPatch)
    {
        try
        {
            await DatabaseSchemaPatcher.ApplyAsync(db, logger);
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

app.Run();
