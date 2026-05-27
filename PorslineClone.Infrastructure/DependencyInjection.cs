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

namespace PorslineClone.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SmsGatewayOptions>(configuration.GetSection(SmsGatewayOptions.SectionName));
        services.Configure<DatabaseStartupOptions>(configuration.GetSection(DatabaseStartupOptions.SectionName));
        services.Configure<ContractSignatureOptions>(configuration.GetSection(ContractSignatureOptions.SectionName));
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
        services.AddScoped<ApprovalReminderService>();
        services.AddHostedService<ApprovalReminderBackgroundService>();
        services.AddSingleton<IDocxToPdfConverter, DocxToPdfConverterService>();
        services.AddScoped<IContractDocumentGenerator, ContractDocumentGeneratorService>();
        services.AddScoped<ContractTemplateFileStorageService>();
        services.AddScoped<ContractDocumentTemplateService>();
        return services;
    }
}
