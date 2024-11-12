using System.Net.Sockets;

namespace ESR.Shared;

public static class NetworkMessenger
{
    public class Response(PacketReader reader)
    {
        public OpCodes OpCode { get; init; } = reader.OpCode;
        public string[] Arguments { get; init; } = reader.Arguments;
    }
    
    public static Response Get(string ip, int port, OpCodes opCode, params string[] args)
    {
        var client = SendInternal(ip, port, opCode, args);
        return new Response(new PacketReader(client.GetStream()));
    }
    
    public static async Task<Response> GetAsync(string ip, int port, OpCodes opCode, params string[] args)
    {
        var client = await SendAsyncInternal(ip, port, opCode, args);
        return new Response(new PacketReader(client.GetStream()));
    }
    
    public static void Send(string ip, int port, OpCodes opCode, params string[] args)
    {
        SendInternal(ip, port, opCode, args);
    }
    
    public static async Task SendAsync(string ip, int port, OpCodes opCode, params string[] args)
    {
        await SendAsyncInternal(ip, port, opCode, args);
    }
    
    private static TcpClient SendInternal(string ip, int port, OpCodes opCode, params string[] args)
    {
        var client = new TcpClient(ip, port);
        using var stream = client.GetStream();
        var packetBuilder = new PacketBuilder().WriteOpCode(opCode);
        
        for (var i = 0; i < args.Length; i++)
        {
            packetBuilder.WriteArgument(args[i]);
        }
        
        stream.Write(packetBuilder.Packet);
        return client;
    }
    
    private static async Task<TcpClient> SendAsyncInternal(string ip, int port, OpCodes opCode, params string[] args)
    {
        var client = new TcpClient(ip, port);
        await using var stream = client.GetStream();
        var packetBuilder = new PacketBuilder().WriteOpCode(opCode);
        
        for (var i = 0; i < args.Length; i++)
        {
            packetBuilder.WriteArgument(args[i]);
        }
        
        await stream.WriteAsync(packetBuilder.Packet);
        return client;
    }
}