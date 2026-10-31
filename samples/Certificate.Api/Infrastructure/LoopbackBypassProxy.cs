using System.Net;

namespace Certificate.Api.Infrastructure;

/// <summary>
/// 访问本机（localhost / 127.0.0.1）时不走代理，其余请求沿用系统代理设置。
/// 开发机配置了 HTTP_PROXY 但 NO_PROXY 未包含 localhost 时，演示用的 Webhook / HTTP 动作调用本服务会被代理拦截。
/// </summary>
public sealed class LoopbackBypassProxy(IWebProxy inner) : IWebProxy
{
    public ICredentials? Credentials
    {
        get => inner.Credentials;
        set => inner.Credentials = value;
    }

    public Uri? GetProxy(Uri destination) => inner.GetProxy(destination);

    public bool IsBypassed(Uri host) => host.IsLoopback || inner.IsBypassed(host);
}
