namespace Approval.Certificate;

/// <summary>随模块发布的默认流程。宿主可用自己的同名定义覆盖，发布时机由宿主决定。</summary>
public static class CertificateFlowTemplates
{
    public static IReadOnlyDictionary<string, string> ReadDefaults()
    {
        const string prefix = "Approval.Certificate.Flows.";
        var assembly = typeof(CertificateFlowTemplates).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToDictionary(name => name[prefix.Length..^5], name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"模块流程资源不存在：{name}");
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }, StringComparer.Ordinal);
    }
}
