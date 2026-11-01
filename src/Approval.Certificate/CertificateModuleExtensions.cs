using Approval.Core;
using global::Certificate.Api.Certificates;
using global::Certificate.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Approval.Certificate;

/// <summary>注册证业务的统一注册入口；DbContext、认证、访问策略和后台任务启动由宿主配置。</summary>
public static class CertificateModuleExtensions
{
    public static ApprovalBuilder AddCertificateModule(this ApprovalBuilder approval)
    {
        var services = approval.Services;
        services.TryAddSingleton(new CertificateOptions());
        services.AddScoped<CertificateService>();
        services.AddScoped<CertificateDataValidator>();
        services.AddScoped<CertificateWorkflowPolicy>();
        services.AddScoped<CertificateQueryService>();
        services.AddScoped<CertificateAccessPolicy>();
        services.AddScoped<CertificateDatabase>();
        services.AddScoped<CertificateDiagnostics>();
        services.AddSingleton<CertificateMonitor>();
        return approval.AddBusinessHandler<CertificateApprovalHandler>()
            .AddActionHandler<CertificateExpiryCheckAction>();
    }
}
