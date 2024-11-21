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
        private static Dictionary<int, TcpClient> tcpClients = [];
        private static SBT sbt;

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
            networkGraph.UpdateNode(networkGraph.GetNode(0).Alias[0], true);
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
                    var tcpClient = await listener.AcceptTcpClientAsync();

                    var friendly = networkGraph.Nodes.Find(node =>
                        node.HasAlias(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient))));
                    if (friendly == null)
                    {
                        Console.WriteLine(
                            $"[Listener] Node {tcpClient.Client.RemoteEndPoint} is not a friendly node. Closing connection...");
                        tcpClient.Close();
                    }
                    else
                    {
                        tcpClients.Add(friendly.Id, tcpClient);
                        _ = Task.Run(() => HandleNodeConnection(tcpClient));
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
                        networkGraph.UpdateNode(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient)), false);
                        tcpClients.Remove(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient)));
                        break;
                    }
                    else
                    {
                        switch (opCode)
                        {
                            case OpCodes.Bootstrap:
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

                                    List<NodeConnection> nodeConnections = [];
                                    foreach (var nodeConnection in node.Connections)
                                    {
                                        var nodeConnectionNode =
                                            networkGraph.GetAliasNode(Utils.IpToInt32(nodeConnection));
                                        if (nodeConnectionNode != null)
                                        {
                                            nodeConnections.Add(new NodeConnection()
                                            {
                                                Id = nodeConnectionNode.Id,
                                                Aliases = Utils.Int32ToIp(nodeConnectionNode.Alias),
                                                Connected = nodeConnectionNode.IsConnected
                                            });
                                        }
                                    }

                                    var connections = new NodeResponse
                                    {
                                        Connections = nodeConnections
                                    };

                                    Console.WriteLine(
                                        $"[Listener] Node {tcpClient.Client.RemoteEndPoint} requested nodes");
                                    var packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.Bootstrap)
                                        .WriteArgument(JsonSerializer.Serialize(connections));
                                    stream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                                    found = true;

                                    var networkGraphNode =
                                        networkGraph.GetAliasNode(
                                            Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient)));
                                    if (networkGraphNode == null) continue;

                                    networkGraphNode.IsConnected = true;

                                    foreach (var connection in nodeConnections)
                                    {
                                        if (!tcpClients.TryGetValue(connection.Id, out var client)) continue;
                                        stream = client.GetStream();
                                        var id = networkGraphNode.Id;
                                        packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.NodeUpdate)
                                            .WriteArgument(id.ToString()).WriteArgument("1");
                                        stream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                                    }
                                    
                                    sbt = SBT.BuildSBT(networkGraph.GetNode(0), networkGraph);
                                    foreach (var sbtnode in sbt.AdjacencyList.Keys)
                                    {
                                        Console.WriteLine($"Node {sbtnode.Id} has children: {string.Join(", ", sbt.GetChildren(sbtnode).Select(x => x.Id))}");
                                        
                                        if (!tcpClients.TryGetValue(sbtnode.Id, out var client)) continue;
                                        Console.WriteLine($"[Tracker] Sending ForwardTo to node {sbtnode.Id}");
                                        stream = client.GetStream();
                                        packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.ForwardTo).WriteArguments(sbt.GetChildren(sbtnode).Select(x => x.Id.ToString()).ToArray());
                                        stream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                                    }


                                    break;
                                }

                                if (!found)
                                {
                                    Console.WriteLine(
                                        $"[Listener] Client {tcpClient.Client.RemoteEndPoint} requested nodes but is not in the list");
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