using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Persistence;

namespace Approval.Core.Engine;

public sealed record DefinitionImportReceipt(string Key, long DraftRevision);

public sealed partial class ApprovalDefinitionService
{
    /// <summary>Checks whether the package can become a new draft in this host, without reserving the key.</summary>
    public async Task<DefinitionTransferSource> ValidateImportAsync(string key, ReadOnlyMemory<byte> file, CancellationToken ct = default)
    {
        var (source, _, _) = await CheckImportAsync(key, file, ct);
        return source;
    }

    /// <summary>Creates an unpublished draft for an unused key inside a managed approval command.</summary>
    public Task<DefinitionImportReceipt> ImportDraftAsync(string key, ReadOnlyMemory<byte> file, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(ApprovalWriteOwner.Definitions, async token =>
        {
            var (source, flow, json) = await CheckImportAsync(key, file, token);
            var management = CreateManagement(key, source.Name);
            await SaveDraftAsync(management, source.Name, flow, json, null, $"导入自 {source.Key} v{source.Version}", token);
            return new DefinitionImportReceipt(key, management.DraftRevision);
        }, ct);

    private async Task<(DefinitionTransferSource Source, FlowDefinition Flow, string Json)> CheckImportAsync(string key, ReadOnlyMemory<byte> file, CancellationToken ct)
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionDesign, new() { DefinitionKey = key }, ct: ct);
        var package = DefinitionTransferPackage.Parse(file);
        ValidateIdentity(key, package.Source.Name);
        await RequireUnusedDefinitionKeyAsync(key, ct);
        var flow = FlowDefinition.ParseImported(package.Definition.GetRawText());
        _ = CategoryOf(flow);
        // Optional module validation may materialize a typed form. Keep the original snapshot in the draft.
        var json = SerializeDefinition(flow);
        if (json.Length > MaxDraftCharacters) throw InvalidImport("设计草稿超过 1000000 字符");
        var errors = ValidateCore(flow);
        if (errors.Count != 0) throw InvalidImport("流程定义校验失败：" + string.Join("；", errors));
        return (package.Source, flow, json);
    }

    private static ApprovalException InvalidImport(string message) => new(message) { Code = ApprovalErrorCode.InvalidDefinition };
}
