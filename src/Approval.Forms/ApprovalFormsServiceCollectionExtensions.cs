using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Forms.Attachments;
using Approval.Forms.Runtime;
using Approval.Forms.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Approval.Forms;

public static class ApprovalFormsServiceCollectionExtensions
{
    public static ApprovalBuilder AddApprovalForms(this ApprovalBuilder builder, Action<ApprovalFormOptions>? configure = null)
    {
        builder.Services.AddApprovalForms(configure);
        return builder;
    }

    public static IServiceCollection AddApprovalForms(this IServiceCollection services, Action<ApprovalFormOptions>? configure = null)
    {
        var options = new ApprovalFormOptions(); configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddScoped<IApprovalDefinitionExtension, FormDefinitionValidator>();
        services.TryAddScoped<IApprovalDataProjection, FormDataProjection>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApprovalResourceResolver, FormResourceResolver>());
        services.TryAddScoped<ApprovalFormService>();
        services.TryAddScoped<ApprovalFileService>();
        services.TryAddScoped<IApprovalFileStore, MissingApprovalFileStore>();
        services.AddScoped<IApprovalBusinessHandler>(_ => new FormApprovalHandler(options));
        return services;
    }

    public static ApprovalBuilder AddFileStore<T>(this ApprovalBuilder builder) where T : class, IApprovalFileStore
    {
        builder.Services.Replace(ServiceDescriptor.Scoped<IApprovalFileStore, T>());
        return builder;
    }
}
