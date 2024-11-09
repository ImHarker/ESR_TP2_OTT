using System.Net;
using System.Net.Sockets;

namespace ESR.Shared;

public static class Utils
{
    public static string? GetIPAddressFromTcpClient(TcpClient tcpClient)
    {
        return (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString().Split(":")[0];
    } 
}