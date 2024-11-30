using System.Collections.Concurrent;
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
        private static ConcurrentDictionary<int, TcpClient> tcpClients = [];
        private static SBT sbt;
        private static ConcurrentDictionary<NodeNet.Node, ConcurrentQueue<Metrics>> s_MetricsCalc = new();

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
                        if (!tcpClients.TryAdd(friendly.Id, tcpClient))
                        {
                            Console.WriteLine($"[Listener] Failed to add TcpClient for node {friendly.Id}");
                            tcpClient.Close();
                        }
                        else
                        {
                            _ = Task.Run(() => HandleNodeConnection(tcpClient));
                        }
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
            Console.WriteLine("[Listener] Handling Node Connection");
            try {
                var stream = tcpClient.GetStream();

                while (true)
                {
                    _ = new PacketReader(stream).GetOpCode(out var opCode).GetArguments(out var args);
                    
                    Console.WriteLine($"[Listener] Received OpCode: {opCode:X} - {opCode} - {string.Join(", ", args)}");

                    if (opCode == OpCodes.Disconnect)
                    {
                        Console.WriteLine($"[Listener] Client {tcpClient.Client.RemoteEndPoint} disconnected");
                        networkGraph.UpdateNode(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient)), false);
                        var ipAddressInt = Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient));
                        tcpClients.TryRemove(ipAddressInt, out _);
                        break;
                    }
                    else
                    {
                        switch (opCode)
                        {
                            case OpCodes.Bootstrap:
                                await HandleBootstrap(tcpClient, stream, args);
                                break;
                            
                            case OpCodes.Metrics:
                                HandleMetrics(tcpClient, args);
                                break;
                            default:
                                Console.WriteLine($"[Listener] Unknown OpCode: {opCode} from {tcpClient.Client.RemoteEndPoint}");
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
        
        private static async Task HandleBootstrap(TcpClient tcpClient, NetworkStream stream, string[] args)
        {
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
                                    
                Console.WriteLine(networkGraph.Nodes.Count(x=> x.IsConnected)); 
                if(networkGraph.Nodes.Count(x=> x.IsConnected) < 9) continue;
                NetworkMessenger.StartUdpClient(Consts.UdpPort, async client => {
                    packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.VideoStream).WriteArgument("TESTE");
                    var ipstr = Utils.Int32ToIp(networkGraph.Nodes[1].Alias[0]);
                    var ip = IPAddress.Parse(ipstr);
                        
                    var ipEndpoint = new IPEndPoint(ip, Consts.UdpPort);
                    Console.WriteLine("Streaming Packet!");
                    while (true) {
                        await client.SendAsync(packetBuilder.Packet, packetBuilder.Packet.Length, ipEndpoint);
                    }
                });

                break;
            }

            if (!found)
            {
                Console.WriteLine(
                    $"[Listener] Client {tcpClient.Client.RemoteEndPoint} requested nodes but is not in the list");
                await stream.WriteAsync("[]"u8.ToArray());
            }
        }
        
        private static void HandleMetrics(TcpClient tcpClient, string[] args)
        {
            var nodeId = -1;
            foreach (var node in nodeNet.Nodes) {
                nodeId++;
                var isAlias = false;
                for (var i = 0; i < node.IpAddressAlias.Length; i++) {
                    if (node.IpAddressAlias[i] == Utils.GetIPAddressFromTcpClient(tcpClient)) {
                        isAlias = true;
                        break;
                    }
                }
                if (!isAlias) continue;
                                    
                Console.WriteLine($"[Metrics] Received Metrics from Node {nodeId}");
                                    
                var metrics = JsonSerializer.Deserialize<Metrics>(args[0]);
                if (!s_MetricsCalc.ContainsKey(node)) s_MetricsCalc[node] = new ConcurrentQueue<Metrics>();
                s_MetricsCalc[node].Enqueue(metrics);

                while (s_MetricsCalc[node].Count > 20) {
                    s_MetricsCalc[node].TryDequeue(out _);
                }
                Console.WriteLine($"[Metrics] Node {nodeId}");
                foreach (var metric in s_MetricsCalc[node]) {
                    Console.WriteLine($"\tPacket Loss: {metric.PacketLoss:P}, Avg RTT: {metric.AverageRTT}ms");
                }
            }
            
        }
        
    }
}