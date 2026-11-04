using Approval.Core.Abstractions;
using Approval.Core.Notifications;

namespace Approval.Core.Engine.Extensions;

/// <summary>同一 scope 中共享扩展选择；按需解析各类服务，避免查询元数据或投递时创建无关业务回调。</summary>
internal sealed class ApprovalExtensionCatalog
{
    private readonly Lazy<Dictionary<string, IApproverResolver>> _resolvers;
    private readonly Lazy<Dictionary<string, IApprovalBusinessHandler>> _businessHandlers;
    private readonly Lazy<Dictionary<string, ChannelInfo>> _channels;
    private readonly Lazy<Dictionary<string, ApproverTypeDescriptor>> _approverDescriptors;
    private readonly Lazy<IReadOnlyList<ChannelInfo>> _channelDescriptors;
    private readonly Lazy<HashSet<string>> _channelNames;

    public ApprovalExtensionCatalog(
        Func<IEnumerable<IApproverResolver>> resolvers,
        Func<IEnumerable<IApprovalBusinessHandler>> businessHandlers,
        Func<IEnumerable<ChannelInfo>> channels)
    {
        _resolvers = new(() => LastByKey(resolvers(), r => r.Type));
        _businessHandlers = new(() => LastByKey(businessHandlers(), h => h.BusinessType));
        _channels = new(() => LastByKey(channels(), c => c.Name));
        _approverDescriptors = new(() => Resolvers.ToDictionary(
            pair => pair.Key, pair => pair.Value.Descriptor with { Type = pair.Key }, StringComparer.OrdinalIgnoreCase));
        _channelDescriptors = new(() =>
        [
            new(InAppChannel.Name, InAppChannel.DisplayName),
            .. Channels.Select(pair => new ChannelInfo(pair.Key, pair.Value.DisplayName))
        ]);
        _channelNames = new(() => Channels.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, IApproverResolver> Resolvers => _resolvers.Value;
    public IReadOnlyDictionary<string, IApprovalBusinessHandler> BusinessHandlers => _businessHandlers.Value;
    public IReadOnlyDictionary<string, ChannelInfo> Channels => _channels.Value;
    public IReadOnlyDictionary<string, ApproverTypeDescriptor> ApproverDescriptors => _approverDescriptors.Value;
    public IReadOnlyList<ChannelInfo> ChannelDescriptors => _channelDescriptors.Value;
    public IReadOnlySet<string> ChannelNames => _channelNames.Value;

    private static Dictionary<string, T> LastByKey<T>(IEnumerable<T> registrations, Func<T, string> key)
    {
        var selected = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations) selected[key(registration)] = registration;
        return selected;
    }
}
