namespace Certificate.Api.Certificates;

public sealed class CertificateOptions
{
    public bool DemoMode { get; set; }
    public string BusinessTimeZone { get; set; } = "Asia/Shanghai";
    public TimeSpan MonitorInterval { get; set; } = TimeSpan.FromMinutes(5);
}
