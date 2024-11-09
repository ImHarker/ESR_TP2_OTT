using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ESR.Shared;

namespace ESR.Tracker
{
    public struct NodeResponse
    {
        [JsonPropertyName("connections")]
        public string[] Connections { get; init; }
        [JsonPropertyName("isPOP")]
        public bool IsPOP { get; init; }
    }
    
    public struct Node
    {
        [JsonPropertyName("ip")]
        public string IpAddress { get; init; }
        [JsonPropertyName("connections")]
        public string[] Connections { get; init; }
        [JsonPropertyName("isPOP")]
        public bool IsPOP { get; init; }
    }
    
    public struct NodeNet
    {
        [JsonPropertyName("nodes")]
        public Node[] Nodes { get; init; }
    }
    
    internal static class Program
    {
        private static NodeNet nodeNet;
        private static List<TcpClient> tcpClients = [];
        
        private static async Task Main()
        {
            var json = await File.ReadAllTextAsync("NodeNet.json");
            nodeNet = JsonSerializer.Deserialize<NodeNet>(json);

            await Listen();
        }

        private static async Task Listen()
        {
            var ipEndPoint = new IPEndPoint(IPAddress.Any, Consts.TcpPort);
            var listener = new TcpListener(ipEndPoint);
            listener.Start();
            
            while (true)
            {
                try
                {
                    Console.WriteLine("[Listener] Waiting for connection...");
                    tcpClients.Add(await listener.AcceptTcpClientAsync());
                    Console.WriteLine($"[Listener] Connected to client {tcpClients[^1].Client.RemoteEndPoint}");
                    
                    _ = Task.Run(() => HandleClientConnection(tcpClients[^1]));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Listener] Error: {e.Message}");
                }
            }
        }

        private static async Task HandleClientConnection(TcpClient tcpClient)
        {
            try
            {
                var stream = tcpClient.GetStream();
                var buffer = new byte[1024];

                while (true)
                {
                    int bytesToRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesToRead == 0)
                    {
                        Console.WriteLine($"[Listener] Client {tcpClient.Client.RemoteEndPoint} disconnected");
                        break;
                    }
                    else if (bytesToRead == 1)
                    {
                        var opCode = buffer[0];
                        switch ((OpCodes)opCode)
                        {
                            case OpCodes.GetNodes:
                                var found = false;
                                foreach (var node in nodeNet.Nodes)
                                {
                                    var connections = new NodeResponse
                                    {
                                        Connections = node.Connections,
                                        IsPOP = node.IsPOP
                                    };
                                    if (node.IpAddress == Utils.GetIPAddressFromTcpClient(tcpClient))
                                    {
                                        Console.WriteLine($"[Listener] Node {tcpClient.Client.RemoteEndPoint} requested nodes");
                                        var json = JsonSerializer.Serialize(connections);
                                        var response = Encoding.UTF8.GetBytes(json);
                                        await stream.WriteAsync(response, 0, response.Length);
                                        found = true;
                                        break;
                                    }
                                }

                                if (!found)
                                {
                                    Console.WriteLine($"[Listener] Client {tcpClient.Client.RemoteEndPoint} requested nodes but is not in the list");
                                    await stream.WriteAsync("[]"u8.ToArray());
                                }
                                break;
                            default:
                                Console.WriteLine($"[Listener] Unknown OpCode: {opCode}");
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Listener] Error: {e.Message}");
            }
            finally
            {
                tcpClient.Close();
            }
        }
    }
}