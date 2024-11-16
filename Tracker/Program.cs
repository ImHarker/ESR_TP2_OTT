using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ESR.Shared;

namespace ESR.Tracker
{
    internal static class Program
    {
        private static NodeNet nodeNet;
        private static NetworkGraph networkGraph;
        private static List<TcpClient> tcpClients = [];

        private static async Task Main()
        {
            BootstrapGraph();
            await Listen();
        }

        private static void BootstrapGraph()
        {
            var json = File.ReadAllText("NodeNet.json");
            nodeNet = JsonSerializer.Deserialize<NodeNet>(json);
            networkGraph = new NetworkGraph(nodeNet);
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
                    tcpClients.Add(await listener.AcceptTcpClientAsync());

                    var friendly = networkGraph.Nodes.Find(node => node.HasAlias(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClients[^1])!)));
                    if (friendly == null)
                    {
                        Console.WriteLine($"[Listener] Node {tcpClients[^1].Client.RemoteEndPoint} is not a friendly node. Closing connection...");
                        tcpClients[^1].Close();
                    }
                    else
                    {
                        _ = Task.Run(() => HandleNodeConnection(tcpClients[^1]));
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Listener] Error: {e.Message}");
                }
            }
        }

        private static async Task HandleNodeConnection(TcpClient tcpClient)
        {
            Console.Write("[Listener] Handling Node Connection");
            try
            {
                var stream = tcpClient.GetStream();

                while (true)
                {
                    _ = new PacketReader(stream).GetOpCode(out var opCode).GetArguments(out _);

                    Console.WriteLine($"[Listener] Received OpCode: {opCode:X} - {opCode}");

                    if (opCode == OpCodes.Disconnect)
                    {
                        Console.WriteLine($"[Listener] Client {tcpClient.Client.RemoteEndPoint} disconnected");
                        break;
                    }
                    else
                    {
                        switch (opCode)
                        {
                            case OpCodes.GetNodes:
                                var found = false;
                                foreach (var node in nodeNet.Nodes)
                                {
                                    var isAlias = false;
                                    for (var i = 0; i < node.IpAddressAlias.Length; i++)
                                    {
                                        if (node.IpAddressAlias[i] == Utils.GetIPAddressFromTcpClient(tcpClient))
                                        {
                                            isAlias = true;
                                            break;
                                        }
                                    }

                                    if (!isAlias) continue;

                                    var connections = new NodeResponse
                                    {
                                        Connections = node.Connections,
                                        IsPOP = node.IsPOP
                                    };
                                    Console.WriteLine($"[Listener] Node {tcpClient.Client.RemoteEndPoint} requested nodes");
                                    var packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.GetNodes).WriteArgument(JsonSerializer.Serialize(connections));
                                    stream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                                    found = true;
                                    break;
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