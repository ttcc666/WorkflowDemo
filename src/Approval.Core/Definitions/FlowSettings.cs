namespace Approval.Core.Definitions;

public sealed class FlowSettings
{
    /// <summary>出现在发起中心，用户可直接填写表单发起（表单审批）。默认 false：由业务模块调用引擎发起。</summary>
    public bool Startable { get; set; }

    /// <summary>发起中心的分组，如“人事”“财务”。</summary>
    public string? Category { get; set; }

    public string? Description { get; set; }

    /// <summary>允许发起的角色，空表示所有人。</summary>
    public List<string> InitiatorRoles { get; set; } = [];

    /// <summary>
    /// 审批标题模板，可用 <c>{流程名}</c>、<c>{申请人}</c>、<c>{发起时间}</c>、<c>{form.字段}</c>；
    /// 为空时为 <c>{流程名} - {申请人}</c>。
    /// </summary>
    public string? TitleTemplate { get; set; }
}
