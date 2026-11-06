using System.Net;
using System.Net.Sockets;

namespace Approval.Core.Actions;

internal static class PublicHttpTransport
{
    private static readonly IPNetwork[] BlockedIpv4 =
    [
        IPNetwork.Parse("0.0.0.0/8"), IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"), IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"), IPNetwork.Parse("192.0.2.0/24"), IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"), IPNetwork.Parse("198.18.0.0/15"), IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"), IPNetwork.Parse("224.0.0.0/3")
    ];

    private static readonly IPNetwork GlobalIpv6 = IPNetwork.Parse("2000::/3");
    private static readonly IPNetwork[] BlockedIpv6 =
    [
        IPNetwork.Parse("2001::/23"), IPNetwork.Parse("2001:db8::/32"), IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("3fff::/20")
    ];

    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !BlockedIpv4.Any(network => network.Contains(address)),
            AddressFamily.InterNetworkV6 => address.ScopeId == 0 && GlobalIpv6.Contains(address) &&
                !BlockedIpv6.Any(network => network.Contains(address)),
            _ => false
        };
    }

    internal static bool IsAllowedAddress(Uri? address) =>
        address is { IsAbsoluteUri: true, Scheme: "https", Port: 443, UserInfo.Length: 0, Fragment.Length: 0, Query.Length: 0 } &&
        (IPAddress.TryParse(address.IdnHost, out var ip)
            ? IsPublic(ip)
            : address.HostNameType == UriHostNameType.Dns && address.IdnHost.TrimEnd('.').Contains('.') &&
              !address.IdnHost.TrimEnd('.').EndsWith(".localhost", StringComparison.OrdinalIgnoreCase));

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        MaxResponseHeadersLength = 16,
        MaxResponseDrainSize = 0,
        MaxConnectionsPerServer = 8,
        ActivityHeadersPropagator = null,
        ConnectCallback = ConnectAsync
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        if (endpoint.Port != 443) throw new HttpRequestException("HTTP action port refused");
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublic(address)))
            throw new HttpRequestException("HTTP action DNS refused");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                // Connect to the exact checked address. TLS still uses the original URL host.
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("HTTP action connection unavailable");
    }
}
