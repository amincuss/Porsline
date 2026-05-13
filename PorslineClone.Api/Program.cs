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
builder.Services.AddSwaggerGen();
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
    options.AddPolicy("responders.read", p => p.RequireClaim("permission", "responders.read"));
    options.AddPolicy("responders.add", p => p.RequireClaim("permission", "responders.add"));
    options.AddPolicy("responders.update", p => p.RequireClaim("permission", "responders.update"));
    options.AddPolicy("responders.send", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim("permission", "responders.send") || ctx.User.HasClaim("permission", "responders.update")));
    options.AddPolicy("usergroups.read", p => p.RequireClaim("permission", "usergroups.read"));
    options.AddPolicy("usergroups.add", p => p.RequireClaim("permission", "usergroups.add"));
    options.AddPolicy("usergroups.update", p => p.RequireClaim("permission", "usergroups.update"));
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("DevCors");

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
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db          = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();

    // اعمال تمام migration ها (شامل HasData seed داده‌های ایستا)
    await db.Database.MigrateAsync();

    // Upsert امن داده‌های مرجع: موارد موجود را نگه می‌دارد، موارد جدید را اضافه می‌کند.
    await DbSeeder.EnsureReferenceDataAsync(db, roleManager);

    // ایجاد کاربر admin فقط یک بار، در صورت عدم وجود
    await DbSeeder.SeedAdminUserAsync(db, userManager);
}

app.Run();
