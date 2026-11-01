using Approval.Core;

namespace Certificate.Api.Certificates;

public sealed class CertificateDataValidator(TimeProvider clock, CertificateOptions options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.BusinessTimeZone);
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), _zone).DateTime);
    public static string NormalizeNumber(string value) => value.Trim().ToUpperInvariant();

    public CertificateData Validate(CertificateData data, bool checkExpiry = true)
    {
        var errors = new Dictionary<string, string[]>();
        string Text(string? value, string key, string label, int max)
        {
            var text = value?.Trim() ?? "";
            if (text.Length == 0) errors[key] = [$"请填写{label}"];
            else if (text.Length > max || text.Any(char.IsControl)) errors[key] = [$"{label}最多 {max} 字且不能包含控制字符"];
            return text;
        }
        var result = data with
        {
            CertNo = Text(data.CertNo, "certNo", "注册证编号", 100),
            Name = Text(data.Name, "name", "注册证名称", 200),
            Holder = Text(data.Holder, "holder", "持有人", 200),
            CertType = data.CertType?.Trim() ?? ""
        };
        if (result.CertType is not ("Normal" or "Special")) errors["certType"] = ["证书类型无效"];
        if (result.IssueDate == default) errors["issueDate"] = ["请填写发证日期"];
        if (result.ExpireDate == default) errors["expireDate"] = ["请填写到期日期"];
        else if (result.ExpireDate <= result.IssueDate) errors["expireDate"] = ["到期日期必须晚于发证日期"];
        else if (checkExpiry && result.ExpireDate < Today) errors["expireDate"] = ["注册证已过期，不能登记为有效证书"];
        if (errors.Count > 0) throw new ApprovalValidationException(errors);
        return result;
    }

    public static string ValidateReason(string? reason)
    {
        var text = reason?.Trim() ?? "";
        if (text.Length is 0 or > 1000)
            throw new ApprovalValidationException(new Dictionary<string, string[]> { ["reason"] = ["请填写 1–1000 字的注销原因"] });
        return text;
    }
}
