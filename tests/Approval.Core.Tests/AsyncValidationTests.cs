using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Engine;

namespace Approval.Core.Tests;

public sealed class AsyncValidationTests
{
    private sealed class PermissionGate : IApprovalPermissionProvider
    {
        public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
        {
            Assert.Equal(ApprovalPermissions.DefinitionValidate, request.Permission);
            Entered.TrySetResult();
            return Decision.Task.WaitAsync(ct);
        }
    }

    private sealed class Extension : IApprovalDefinitionExtension
    {
        public int Calls;
        public IReadOnlySet<string>? UserFields(FlowDefinition definition) => null;
        public void Validate(FlowDefinition definition, List<string> errors) { Calls++; errors.Add("extension-result"); }
        public IReadOnlyList<string> Warnings(FlowDefinition definition) => [];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WaitsForPermissionWithoutRunningValidation(bool allowed)
    {
        var permission = new PermissionGate(); var extension = new Extension();
        // This test controls authorization with a gate; its separate timeout tests own the short deadline.
        await using var host = new TestHost(o => o.PermissionTimeout = TimeSpan.FromMinutes(1), build: b => {
            b.Services.AddSingleton<IApprovalPermissionProvider>(permission);
            b.Services.AddSingleton<IApprovalDefinitionExtension>(extension);
        });
        var result = host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalDefinitionService>()
            .ValidateAsync(FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob"))));
        await permission.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(result.IsCompleted); Assert.Equal(0, extension.Calls);
        permission.Decision.SetResult(allowed);
        if (allowed)
        {
            Assert.Equal(new[] { "extension-result" }, await result.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(1, extension.Calls);
        }
        else
        {
            Assert.Equal(ApprovalErrorCode.Forbidden, (await Assert.ThrowsAsync<ApprovalException>(() => result)).Code);
            Assert.Equal(0, extension.Calls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_CancellationAndPermissionTimeoutDoNotRunValidation(bool timeout)
    {
        var permission = new PermissionGate(); var extension = new Extension();
        await using var host = new TestHost(o => o.PermissionTimeout = timeout ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(5), b => {
            b.Services.AddSingleton<IApprovalPermissionProvider>(permission);
            b.Services.AddSingleton<IApprovalDefinitionExtension>(extension);
        });
        using var cancellation = new CancellationTokenSource();
        var result = host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalDefinitionService>()
            .ValidateAsync(FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob")), cancellation.Token));
        await permission.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (timeout)
            Assert.Equal(ApprovalErrorCode.AuthorizationUnavailable, (await Assert.ThrowsAsync<ApprovalException>(() => result.WaitAsync(TimeSpan.FromMinutes(1)))).Code);
        else
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        }
        Assert.Equal(0, extension.Calls);
    }
}
