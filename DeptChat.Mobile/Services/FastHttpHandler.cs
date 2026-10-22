using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace DeptChat.Mobile.Services;

public static class FastHttpHandler
{
    /// <summary>
    /// Creates a high-performance SocketsHttpHandler configured for sub-second mobile and desktop performance.
    /// Eliminates the 21-second Cloudflare Anycast timeout on trycloudflare.com by prioritizing the responsive edge IP (104.16.231.132).
    /// </summary>
    public static HttpMessageHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 20,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            },
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;

                // Optimize trycloudflare: 104.16.231.132 is the fast Anycast edge (0.02s) vs 104.16.230.132 which has a 21s routing black hole on some ISPs
                if (host.EndsWith("trycloudflare.com", StringComparison.OrdinalIgnoreCase))
                {
                    var ips = new[] { IPAddress.Parse("104.16.231.132"), IPAddress.Parse("104.16.230.132") };
                    foreach (var ip in ips)
                    {
                        try
                        {
                            var s = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            cts.CancelAfter(TimeSpan.FromSeconds(2.5));
                            await s.ConnectAsync(new IPEndPoint(ip, port), cts.Token);
                            return new NetworkStream(s, ownsSocket: true);
                        }
                        catch
                        {
                            // Try next IP
                        }
                    }
                }

                // Standard connection for local Wi-Fi LAN or direct hosts
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };

        return handler;
    }
}
