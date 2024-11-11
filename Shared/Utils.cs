using System.Net;
using System.Net.Sockets;

namespace ESR.Shared;

public static class Utils
{
    public static string? GetIPAddressFromTcpClient(TcpClient tcpClient)
    {
        return (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString().Split(":")[0];
    }
    
    public static int IpToInt32(string ip)
    {
        var split = ip.Split(".");
        return (int.Parse(split[0]) << 24) | (int.Parse(split[1]) << 16) | (int.Parse(split[2]) << 8) | int.Parse(split[3]);
    } 
}