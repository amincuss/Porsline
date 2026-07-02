using System.Text.Json;

namespace PorslineClone.Api.Middleware;

/// <summary>خطاهای هندل‌نشده را JSON برمی‌گرداند تا CORS و فرانت پیام قابل‌خواندن داشته باشند.</summary>
public sealed class ApiExceptionHandlingMiddleware(RequestDelegate next, ILogger<ApiExceptionHandlingMiddleware> logger, IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(ex, "Unhandled exception after response started: {Method} {Path}", context.Request.Method, context.Request.Path);
                throw;
            }

            logger.LogError(ex, "Unhandled API exception: {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";

            var message = ResolveClientMessage(ex);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message }), context.RequestAborted);
        }
    }

    private string ResolveClientMessage(Exception ex)
    {
        if (IsDatabaseSchemaMismatch(ex))
        {
            return "ساختار دیتابیس با نسخهٔ API هم‌خوان نیست. API را ری‌استارت کنید تا SchemaPatch اعمال شود، یا ستون‌های جدید را دستی اضافه کنید.";
        }

        if (env.IsDevelopment())
            return ex.Message;

        return "خطای داخلی سرور. جزئیات در لاگ API ثبت شده است.";
    }

    private static bool IsDatabaseSchemaMismatch(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var text = current.Message;
            if (text.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SentByUserId", StringComparison.OrdinalIgnoreCase)
                || text.Contains("OnlyIncompleteSubmissions", StringComparison.OrdinalIgnoreCase)
                || text.Contains("NestedFieldsJson", StringComparison.OrdinalIgnoreCase)
                || text.Contains("IsDeleted", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Gender", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public static class ApiExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseApiExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ApiExceptionHandlingMiddleware>();
}
