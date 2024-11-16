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
    
    public static int[] IpAliasToInt32(string[] ipAlias)
    {
        var result = new int[ipAlias.Length];
        for (var i = 0; i < ipAlias.Length; i++)
        {
            result[i] = IpToInt32(ipAlias[i]);
        }

        return result;
    }
    
    public static int[] IpAliasToInt32(List<string> ipAlias)
    {
        var result = new int[ipAlias.Count];
        for (var i = 0; i < ipAlias.Count; i++)
        {
            result[i] = IpToInt32(ipAlias[i]);
        }

        return result;
    }
}