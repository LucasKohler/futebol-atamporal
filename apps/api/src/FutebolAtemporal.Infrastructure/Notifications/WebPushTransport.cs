using System.Net;
using System.Net.Sockets;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Infrastructure.Notifications;

public static class WebPushTransport
{
    public static SocketsHttpHandler CreateHandler(Func<string, CancellationToken, Task<IPAddress[]>> resolve)
        => new()
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            // O .NET 10 omite o DiagnosticsHandler deste cliente. O caminho da URL contém o token do aparelho.
            ActivityHeadersPropagator = null,
            ConnectCallback = async (context, ct) =>
            {
                var target = await ResolveDestinationAsync(context.DnsEndPoint, resolve, ct);
                var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    // Usa o IP validado diretamente. O nome original continua sendo usado no TLS.
                    await socket.ConnectAsync(new IPEndPoint(target, 443), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

    public static async Task<IPAddress> ResolveDestinationAsync(
        DnsEndPoint destination, Func<string, CancellationToken, Task<IPAddress[]>> resolve, CancellationToken ct)
    {
        if (destination.Port != 443 || !NotificationPolicies.IsAllowedPushHost(destination.Host))
            throw new HttpRequestException("Destino de push não autorizado.");

        var addresses = await resolve(destination.Host, ct);
        var target = addresses.FirstOrDefault(NotificationPolicies.IsPublicAddress)
            ?? throw new HttpRequestException("Destino de push resolve para endereço não público.");
        return target.IsIPv4MappedToIPv6 ? target.MapToIPv4() : target;
    }
}
