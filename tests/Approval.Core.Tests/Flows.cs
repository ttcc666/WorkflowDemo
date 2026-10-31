namespace Approval.Core.Tests;

/// <summary>测试用流程定义。</summary>
public static class Flows
{
    /// <summary>证书审核：部门审核 → 质量审核 →（特种证书）安全审核 → 负责人审批。</summary>
    public const string Certificate = """
    {
      "startNodeId": "start",
      "nodes": {
        "start":   { "name": "提交", "type": "Start", "next": "dept" },
        "dept":    { "name": "部门审核", "type": "Approval", "mode": "Any",
                     "approvers": [{ "type": "Role", "value": "DeptReviewer" }], "next": "quality" },
        "quality": { "name": "质量审核", "type": "Approval", "mode": "Any",
                     "approvers": [{ "type": "Role", "value": "QualityManager" }], "next": "cond" },
        "cond":    { "name": "证书类型", "type": "Condition", "branches": [
                       { "priority": 1, "expression": "certType == \"Special\"", "next": "safety" },
                       { "priority": 99, "expression": null, "next": "leader" } ] },
        "safety":  { "name": "安全审核", "type": "Approval", "mode": "Any",
                     "approvers": [{ "type": "Role", "value": "SafetyOfficer" }], "next": "leader" },
        "leader":  { "name": "负责人审批", "type": "Approval", "mode": "Any",
                     "approvers": [{ "type": "Role", "value": "Leader" }], "next": "cc" },
        "cc":      { "name": "抄送发起人", "type": "Cc",
                     "approvers": [{ "type": "Initiator", "value": "" }], "next": "end" },
        "end":     { "name": "结束", "type": "End" }
      }
    }
    """;

    public static string SingleNode(string mode, string approverType = "Role", string value = "Reviewer",
        string emptyPolicy = "Error") => $$"""
    {
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "{{mode}}", "emptyApproverPolicy": "{{emptyPolicy}}",
                    "approvers": [{ "type": "{{approverType}}", "value": "{{value}}" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    public const string AmountCondition = """
    {
      "startNodeId": "start",
      "nodes": {
        "start": { "name": "提交", "type": "Start", "next": "cond" },
        "cond":  { "name": "金额判断", "type": "Condition", "branches": [
                     { "priority": 1, "expression": "amount > 5000", "next": "big" },
                     { "priority": 2, "expression": null, "next": "small" } ] },
        "big":   { "name": "大额审批", "type": "Approval", "approvers": [{ "type": "User", "value": "boss" }], "next": "end" },
        "small": { "name": "小额审批", "type": "Approval", "approvers": [{ "type": "User", "value": "manager" }], "next": "end" },
        "end":   { "name": "结束", "type": "End" }
      }
    }
    """;
}
