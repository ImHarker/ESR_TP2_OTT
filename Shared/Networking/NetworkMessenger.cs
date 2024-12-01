using System.Net.Sockets;

namespace ESR.Shared;

public class Response(PacketReader reader)
{
    public OpCodes OpCode { get; } = reader.OpCode;
    public string[] Arguments { get; } = reader.Arguments;
    
    public override string ToString()
    {
        var str = $"OpCode: {OpCode}";
        for (var i = 0; i < Arguments.Length; i++)
        {
            str += $"\nArg{i}: {Arguments[i]}";
        }

        return str;
    }
}

public static class NetworkMessenger
{
    #region TCP
    
    public static Dictionary<int, TcpClient> TcpClients { get; } = new();
    private static Dictionary<int, UdpClient> UdpClients { get; } = new();

    public static Response Get(string ip, int port, bool dispose)
    {
        var client = EstablishTcpConnection(ip, port);
        var response = new Response(new PacketReader(client.GetStream()));
        if (dispose) DisposeTcpClient(ip);
        return response;
    }
    
    public static Response Get(string ip, int port, OpCodes opCode, bool dispose, params string[] args)
    {
        var client = SendInternal(ip, port, opCode, args);
        var response = new Response(new PacketReader(client.GetStream()));
        if (dispose) DisposeTcpClient(ip);
        return response;
    }

    public static async Task<Response> GetAsync(string ip, int port, OpCodes opCode, bool dispose, params string[] args)
    {
        var client = await SendAsyncInternal(ip, port, opCode, args);
        var response = new Response(new PacketReader(client.GetStream()));
        if (dispose) DisposeTcpClient(ip);
        return response;
    }

    public static void Send(string ip, int port, OpCodes opCode, bool dispose, params string[] args)
    {
        SendInternal(ip, port, opCode, args);
        if (dispose) DisposeTcpClient(ip);
    }

    public static async Task SendAsync(string ip, int port, OpCodes opCode, bool dispose, params string[] args)
    {
        await SendAsyncInternal(ip, port, opCode, args);
        if (dispose) DisposeTcpClient(ip);
    }

    private static TcpClient SendInternal(string ip, int port, OpCodes opCode, params string[] args)
    {
        var client = EstablishTcpConnection(ip, port); 
        var packetBuilder = new PacketBuilder().WriteOpCode(opCode);

        for (var i = 0; i < args.Length; i++)
        {
            packetBuilder.WriteArgument(args[i]);
        }

        client.GetStream().Write(packetBuilder.Packet);
        return client;
    }

    private static async Task<TcpClient> SendAsyncInternal(string ip, int port, OpCodes opCode, params string[] args)
    {
        var client = EstablishTcpConnection(ip, port);
        var stream = client.GetStream();
        var packetBuilder = new PacketBuilder().WriteOpCode(opCode);

        for (var i = 0; i < args.Length; i++)
        {
            packetBuilder.WriteArgument(args[i]);
        }
        
        await stream.WriteAsync(packetBuilder.Packet);
        return client;
    }

    private static TcpClient EstablishTcpConnection(string ip, int port)
    {
        if (TcpClients.TryGetValue(Utils.IpToInt32(ip), out var tcpClient)) return tcpClient;

        var client = new TcpClient(ip, port);
        TcpClients[Utils.IpToInt32(ip)] = client;
        return client;
    }

    public static void DisposeTcpClient(string ip)
    {
        if (TcpClients.Remove(Utils.IpToInt32(ip), out var client)) client.Close();
    }
    
    #endregion
    
    #region UDP

    public static void StartUdpClient(int port, Func<UdpClient, Task> action)
    {
        if (UdpClients.TryGetValue(port, out _)) return;
        
        var udpClient = new UdpClient(port);
        udpClient.Client.ReceiveBufferSize = 1024 * 1024 * 5; 
        udpClient.Client.SendBufferSize = 1024 * 1024 * 5; 
        UdpClients[port] = udpClient;
        Task.Run(() => action(udpClient));
    }
    
    public static void DisposeUdpClient(int port)
    {
        if (TcpClients.Remove(port, out var client))
        {
            client.Close();
        }
    }
        
    #endregion
}