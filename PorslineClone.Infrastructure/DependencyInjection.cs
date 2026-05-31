using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Auth;
using PorslineClone.Infrastructure.Options;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.ContractTemplates;
using PorslineClone.Infrastructure.Services.Contracts;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SmsGatewayOptions>(configuration.GetSection(SmsGatewayOptions.SectionName));
        services.Configure<DatabaseStartupOptions>(configuration.GetSection(DatabaseStartupOptions.SectionName));
        services.Configure<ContractSignatureOptions>(configuration.GetSection(ContractSignatureOptions.SectionName));
        services.Configure<PersianTextNormalizerOptions>(configuration.GetSection(PersianTextNormalizerOptions.SectionName));
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddIdentityCore<AppUser>()
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddHttpClient<ISmsSender, SmsSender>();
        services.AddScoped<IInboxMessageService, InboxMessageService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IFrontendUrlResolver, FrontendUrlResolver>();
        services.AddScoped<ContractFileStorageService>();
        services.AddScoped<UserSignatureStorageService>();
        services.AddScoped<ContractApprovalStampService>();
        services.AddScoped<ContractApprovalLinkService>();
        services.AddScoped<ContractActionLinkService>();
        services.AddScoped<ContractPostApprovalService>();
        services.AddScoped<ContractWorkflowProcessor>();
        services.AddScoped<FormPostApprovalService>();
        services.AddScoped<FormWorkflowProcessor>();
        services.AddScoped<FormWorkflowRejectionService>();
        services.AddScoped<FormDispatchSubmissionNotifier>();
        services.AddScoped<FormSubmissionApprovalLinkService>();
        services.AddScoped<FormActionLinkService>();
        services.AddScoped<ApprovalReminderService>();
        services.AddHostedService<ApprovalReminderBackgroundService>();
        services.AddSingleton<ILibreOfficeDocumentService, LibreOfficeDocumentService>();
        services.AddSingleton<IDocxToPdfConverter, DocxToPdfConverterService>();
        services.AddScoped<IContractDocumentGenerator, ContractDocumentGeneratorService>();
        services.AddScoped<ContractTemplateFileStorageService>();
        services.AddScoped<ContractDocumentTemplateService>();
        services.AddScoped<Services.FormWordTemplates.FormWordTemplateFileStorage>();
        services.AddScoped<Services.FormWordTemplates.FormWordTemplateService>();
        services.AddScoped<Services.FormWordTemplates.FormWordBatchExportService>();
        services.AddScoped<IFormWordBatchExportHangfireJob, Services.FormWordTemplates.FormWordBatchExportHangfireJob>();
        services.AddScoped<DocumentFileStorageService>();
        services.AddSingleton<FarsiTextNormalizer>();
        services.AddSingleton<IFarsiTextNormalizer>(sp => sp.GetRequiredService<FarsiTextNormalizer>());
        services.AddSingleton<ITextExtractor, PdfFileTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxFileTextExtractor>();
        services.AddSingleton<TextExtractorResolver>();
        services.AddSingleton<DocumentTextExtractionQueue>();
        services.AddSingleton<IDocumentTextExtractionQueue>(sp => sp.GetRequiredService<DocumentTextExtractionQueue>());
        services.AddScoped<IDocumentTextExtractionProcessor, DocumentTextExtractionProcessor>();
        services.AddScoped<IDocumentContentSearchService, DocumentContentSearchService>();
        services.AddHostedService<DocumentTextExtractionBackgroundService>();
        services.AddSingleton<PersianTextNormalizer>();
        services.AddSingleton<IPersianTextNormalizer>(sp => sp.GetRequiredService<PersianTextNormalizer>());
        services.AddScoped<IContractExtractAndIndexJob, ContractExtractAndIndexJob>();
        services.AddScoped<IContractContentSearchService, ContractContentSearchService>();
        return services;
    }
}
