using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ESR.Shared;

namespace ESR.Tracker {
    internal static class Program {
        private static NodeNet nodeNet;
        private static NetworkGraph networkGraph;
        private static ConcurrentDictionary<int, TcpClient> tcpClients = [];
        private static ConcurrentDictionary<string, SBT> sbts = new();
        private static ConcurrentDictionary<(NetworkGraph.Node, NetworkGraph.Node), RttMonitor> rttMonitors = new();
        
        public static ConcurrentDictionary<string, List<NetworkGraph.Node>> ContentDest = new(); // contentId -> POPS


        private static async Task Main() {
            BootstrapGraph();
            await Listen();
        }

        private static void OnNetworkStateChanged() {
            Console.WriteLine("\n\nNetwork state change detected. Recalculating SBTs...\n\n");
            UpdateConnectionWeights();

            foreach (var contentId in sbts.Keys.ToList()) {
                var oldSbt = sbts[contentId];
                sbts[contentId] = SBT.BuildSBT(networkGraph.GetNode(-1), networkGraph, ContentDest[contentId]);
                Console.WriteLine($"SBT for content {contentId} recalculated due to network state change.");

                var oldNodes = oldSbt?.AdjacencyList.Keys.ToHashSet() ?? new HashSet<NetworkGraph.Node>();
                var newNodes = sbts[contentId].AdjacencyList.Keys.ToHashSet();

                // Notify old nodes that they no longer need to forward
                foreach (var oldNode in oldNodes.Except(newNodes)) {
                    if (!tcpClients.TryGetValue(oldNode.Id, out var client)) continue;
                    Console.WriteLine($"[Tracker] Sending empty ForwardTo to old node {oldNode.Id}");
                    var stream = client.GetStream();
                    var packetBuilder = new PacketBuilder()
                        .WriteOpCode(OpCodes.ForwardTo)
                        .WriteArgument(contentId)
                        .WriteArguments(Array.Empty<string>());
                    stream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                }

                // Notify new nodes of their forwarding targets
                foreach (var sbtnode in sbts[contentId].AdjacencyList.Keys) {
                    Console.WriteLine(
                        $"Node {sbtnode.Id} has children: {string.Join(", ", sbts[contentId].GetChildren(sbtnode).Select(x => x.Id))}");

                    if (!tcpClients.TryGetValue(sbtnode.Id, out var client)) continue;
                    Console.WriteLine($"[Tracker] Sending ForwardTo to node {sbtnode.Id}");
                    var stream = client.GetStream();
                    var packetBuilder = new PacketBuilder()
                        .WriteOpCode(OpCodes.ForwardTo)
                        .WriteArgument(contentId)
                        .WriteArguments(sbts[contentId].GetChildren(sbtnode).Select(x => x.Id.ToString()).ToArray());
                    stream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                }
            }
        }

        private static void BootstrapGraph() {
            var json = File.ReadAllText("NodeNet.json");
            nodeNet = JsonSerializer.Deserialize<NodeNet>(json);
            networkGraph = new NetworkGraph(nodeNet);
            networkGraph.UpdateNode(networkGraph.GetNode(0).Alias[0], true);
        }

        private static async Task Listen() {
            var ipEndPoint = new IPEndPoint(IPAddress.Any, Consts.TcpPort);
            var listener = new TcpListener(ipEndPoint);
            listener.Start();

            while (true) {
                try {
                    var tcpClient = await listener.AcceptTcpClientAsync();

                    var friendly = networkGraph.Nodes.Find(node =>
                        node.HasAlias(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient))));
                    if (friendly == null) {
                        
                        _ = new PacketReader(tcpClient.GetStream()).GetOpCode(out var opCode).GetArguments(out var args);
                        if (opCode != OpCodes.ContentMetadata) {
                            Console.WriteLine(
                                $"[Listener] Node {tcpClient.Client.RemoteEndPoint} is not a friendly node. Closing connection...");
                            tcpClient.Close();
                            continue;
                        }
                        Console.WriteLine($"[Listener] Received ContentMetadata from {tcpClient.Client.RemoteEndPoint}");
                        var contentMetadata = new ContentMetadata{ContentIds = [], Pops = []};
                        foreach (var node in networkGraph.Nodes) {
                            if(node.IsPOP) {
                                contentMetadata.Pops.Add(Utils.Int32ToIp(node.Alias));
                            }
                        }

                        foreach (var contentId in sbts.Keys) {
                            contentMetadata.ContentIds.Add(contentId);
                        }
                        
                        var packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.ContentMetadata)
                            .WriteArgument(JsonSerializer.Serialize(contentMetadata));
                        tcpClient.GetStream().Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                        
                        Console.WriteLine($"[Listener] Sent ContentMetadata to {tcpClient.Client.RemoteEndPoint}");
                        tcpClient.Close();
                       
                    }
                    else {
                        if (!tcpClients.TryAdd(friendly.Id, tcpClient)) {
                            Console.WriteLine($"[Listener] Failed to add TcpClient for node {friendly.Id}");
                            tcpClient.Close();
                        }
                        else {
                            _ = Task.Run(() => HandleNodeConnection(tcpClient));
                        }
                    }
                }
                catch (Exception e) {
                    Console.WriteLine($"[Listener] Error: {e.Message}");
                }
            }
        }

        private static readonly object _lock = new object();

        private static async Task HandleNodeConnection(TcpClient tcpClient) {
            Console.WriteLine("[Listener] Handling Node Connection");
            try {
                var stream = tcpClient.GetStream();

                while (true) {
                    _ = new PacketReader(stream).GetOpCode(out var opCode).GetArguments(out var args);

                    // Console.WriteLine($"[Listener] Received OpCode: {opCode:X} - {opCode}");

                    if (opCode == OpCodes.Disconnect) {
                        Console.WriteLine($"[Listener] Client {tcpClient.Client.RemoteEndPoint} disconnected");
                        networkGraph.UpdateNode(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient)), false);
                        var ipAddressInt = Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient));
                        tcpClients.TryRemove(ipAddressInt, out _);
                        break;
                    }
                    else {
                        switch (opCode) {
                            case OpCodes.Bootstrap:
                                await HandleBootstrap(tcpClient, stream, args);
                                break;

                            case OpCodes.Metrics:
                                HandleMetrics(tcpClient, args);
                                break;
                            case OpCodes.ContentMetadata:
                                var contentId = args[0];
                                
                                lock (_lock) {
                                    if (!ContentDest.ContainsKey(contentId)) ContentDest[contentId] = new();
                                }
                                
                                if (!sbts.ContainsKey(contentId)) {
                                    sbts[contentId] = SBT.BuildSBT(networkGraph.GetNode(-1), networkGraph, ContentDest[contentId]);
                                    Console.WriteLine($"[Listener] Created SBT for content {contentId}");
                                }
                                break;
                            case OpCodes.StartStreaming:
                                if (args.Length < 1) {
                                    Console.WriteLine("[Stream] Invalid packet received.");
                                    break;
                                }

                                contentId = args[0];
                                lock (_lock) {
                                    if (!ContentDest.ContainsKey(contentId)) ContentDest[contentId] = new();
                                    var node = networkGraph.Nodes.Find(x => x.Alias.Contains(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient))));
                                    ContentDest[contentId].Add(node);
                                    Console.WriteLine($"[Stream] Added POP to {contentId}");
                                    sbts[contentId] = SBT.BuildSBT(networkGraph.GetNode(-1), networkGraph, ContentDest[contentId]);

                                    foreach (var content in ContentDest.Keys) {
                                        Console.WriteLine($"[HandlePopConnection] Content {content} has {ContentDest[content].Count} POPS");
                                    }

                                    foreach (var content in sbts.Keys) {
                                        Console.WriteLine($"[Stream] Content {content} has {sbts[content].AdjacencyList.Count} nodes in SBT");
                                    }
                                    
                                    // Notify new nodes of their forwarding targets
                                    foreach (var sbtnode in sbts[contentId].AdjacencyList.Keys) {
                                        Console.WriteLine(
                                            $"Node {sbtnode.Id} has children: {string.Join(", ", sbts[contentId].GetChildren(sbtnode).Select(x => x.Id))}");

                                        if (!tcpClients.TryGetValue(sbtnode.Id, out var client)) continue;
                                        Console.WriteLine($"[Tracker] Sending ForwardTo to node {sbtnode.Id}");
                                        var istream = client.GetStream();
                                        var packetBuilder = new PacketBuilder()
                                            .WriteOpCode(OpCodes.ForwardTo)
                                            .WriteArgument(contentId)
                                            .WriteArguments(sbts[contentId].GetChildren(sbtnode).Select(x => x.Id.ToString()).ToArray());
                                        istream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                                    }
                                }
                                break;
                            case OpCodes.StopStreaming:
                                if (args.Length < 1) {
                                    Console.WriteLine("[Stream] Invalid packet received.");
                                    break;
                                }

                                contentId = args[0];
                                lock (_lock) {
                                    if (!ContentDest.ContainsKey(contentId)) continue;
                                    var node = networkGraph.Nodes.Find(x => x.Alias.Contains(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient))));
                                    ContentDest[contentId].Remove(node);
                                    Console.WriteLine($"[Stream] Removed POP from {contentId}");
                                    var oldSbt = sbts[contentId];
                                    var oldNodes = oldSbt?.AdjacencyList.Keys.ToHashSet() ?? new HashSet<NetworkGraph.Node>();
                                    var newNodes = ContentDest[contentId].ToHashSet();
                                    sbts[contentId] = SBT.BuildSBT(networkGraph.GetNode(-1), networkGraph, ContentDest[contentId]);
                                    
                                    foreach (var content in ContentDest.Keys) {
                                        Console.WriteLine($"[Stream] Content {content} has {ContentDest[content].Count} POPS");
                                    }
                                    
                                    foreach (var content in sbts.Keys) {
                                        Console.WriteLine($"[Stream] Content {content} has {sbts[content].AdjacencyList.Count} nodes in SBT");
                                    }
                                    
                                    // Notify old nodes that they no longer need to forward 
                                    
                                    foreach (var oldNode in oldNodes.Except(newNodes)) {
                                        if (!tcpClients.TryGetValue(oldNode.Id, out var client)) continue;
                                        Console.WriteLine($"[Tracker] Sending empty ForwardTo to old node {oldNode.Id}");
                                        var istream = client.GetStream();
                                        var packetBuilder = new PacketBuilder()
                                            .WriteOpCode(OpCodes.ForwardTo)
                                            .WriteArgument(contentId)
                                            .WriteArguments(Array.Empty<string>());
                                        istream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                                    }
                                    
                                    
                                    // Notify new nodes of their forwarding targets
                                    foreach (var sbtnode in sbts[contentId].AdjacencyList.Keys) {
                                        Console.WriteLine(
                                            $"Node {sbtnode.Id} has children: {string.Join(", ", sbts[contentId].GetChildren(sbtnode).Select(x => x.Id))}");

                                        if (!tcpClients.TryGetValue(sbtnode.Id, out var client)) continue;
                                        Console.WriteLine($"[Tracker] Sending ForwardTo to node {sbtnode.Id}");
                                        var istream = client.GetStream();
                                        var packetBuilder = new PacketBuilder()
                                            .WriteOpCode(OpCodes.ForwardTo)
                                            .WriteArgument(contentId)
                                            .WriteArguments(sbts[contentId].GetChildren(sbtnode).Select(x => x.Id.ToString()).ToArray());
                                        istream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                                    }
                                }
                                break;
                            default:
                                Console.WriteLine(
                                    $"[Listener] Unknown OpCode: {opCode} from {tcpClient.Client.RemoteEndPoint}");
                                break;
                        }
                    }
                }
            }
            catch (Exception e) {
                Console.WriteLine($"[Listener] Error: {e.Message}");
            }
            finally {
                tcpClient.Close();
            }
        }

        private static async Task HandleBootstrap(TcpClient tcpClient, NetworkStream stream, string[] args) {
            var found = false;
            foreach (var node in nodeNet.Nodes) {
                var isAlias = false;
                for (var i = 0; i < node.IpAddressAlias.Length; i++) {
                    if (node.IpAddressAlias[i] == Utils.GetIPAddressFromTcpClient(tcpClient)) {
                        isAlias = true;
                        break;
                    }
                }

                if (!isAlias) continue;

                List<NodeConnection> nodeConnections = [];
                foreach (var nodeConnection in node.Connections) {
                    var nodeConnectionNode =
                        networkGraph.GetAliasNode(Utils.IpToInt32(nodeConnection));
                    if (nodeConnectionNode != null) {
                        nodeConnections.Add(new NodeConnection() {
                            Id = nodeConnectionNode.Id,
                            Aliases = Utils.Int32ToIp(nodeConnectionNode.Alias),
                            Connected = nodeConnectionNode.IsConnected
                        });
                    }
                }

                var connections = new NodeResponse {
                    IsPop = node.IsPOP,
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

                foreach (var connection in nodeConnections) {
                    if (!tcpClients.TryGetValue(connection.Id, out var client)) continue;
                    stream = client.GetStream();
                    var id = networkGraphNode.Id;
                    packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.NodeUpdate)
                        .WriteArgument(id.ToString()).WriteArgument("1");
                    stream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                }

                // Initialize SBT for each content
                foreach (var contentId in sbts.Keys) {
                    sbts[contentId] = SBT.BuildSBT(networkGraph.GetNode(-1), networkGraph, ContentDest[contentId]);
                    foreach (var sbtnode in sbts[contentId].AdjacencyList.Keys) {
                        Console.WriteLine(
                            $"Node {sbtnode.Id} has children: {string.Join(", ", sbts[contentId].GetChildren(sbtnode).Select(x => x.Id))}");

                        if (!tcpClients.TryGetValue(sbtnode.Id, out var client)) continue;
                        Console.WriteLine($"[Tracker] Sending ForwardTo to node {sbtnode.Id}");
                        stream = client.GetStream();
                        packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.ForwardTo)
                            .WriteArguments(sbts[contentId].GetChildren(sbtnode).Select(x => x.Id.ToString())
                                .ToArray());
                        stream.Write(packetBuilder.Packet, 0, packetBuilder.Packet.Length);
                    }
                }

                // Console.WriteLine(networkGraph.Nodes.Count(x=> x.IsConnected)); 
                // if(networkGraph.Nodes.Count(x=> x.IsConnected) < 9) continue;
                // NetworkMessenger.StartUdpClient(Consts.UdpPort, async client => {
                //     string largePayload = new string('A', 10000);
                //     packetBuilder = new PacketBuilder().WriteOpCode(OpCodes.VideoStream).WriteArgument(largePayload);
                //     var ipstr = Utils.Int32ToIp(networkGraph.Nodes[1].Alias[0]);
                //     var ip = IPAddress.Parse(ipstr);
                //         
                //     var ipEndpoint = new IPEndPoint(ip, Consts.UdpPort);
                //     Console.WriteLine("Streaming Packet!");
                //     while (true) {
                //         await client.SendAsync(packetBuilder.Packet, packetBuilder.Packet.Length, ipEndpoint);
                //         Thread.Sleep(250);
                //     }
                // });

                break;
            }

            if (!found) {
                Console.WriteLine(
                    $"[Listener] Client {tcpClient.Client.RemoteEndPoint} requested nodes but is not in the list");
                await stream.WriteAsync("[]"u8.ToArray());
            }
        }

        private static void HandleMetrics(TcpClient tcpClient, string[] args) {
            foreach (var node in networkGraph.Nodes) {
                var isAlias = node.Alias.Contains(Utils.IpToInt32(Utils.GetIPAddressFromTcpClient(tcpClient)));
                if (!isAlias) continue;

                // Console.WriteLine($"[Metrics] Received Metrics from Node {node.Id}");

                var metrics = JsonSerializer.Deserialize<Metrics>(args[0]);

                var tuple = (networkGraph.GetNode(Math.Min(node.Id, metrics!.Connection.Id))!,
                    networkGraph.GetNode(Math.Max(node.Id, metrics.Connection.Id))!);

                if (!rttMonitors.ContainsKey(tuple)) {
                    rttMonitors[tuple] = new RttMonitor();
                    rttMonitors[tuple].NetworkStateChanged += OnNetworkStateChanged;
                }

                var rttMonitor = rttMonitors[tuple];

                var stateChangedRtt = rttMonitor.UpdateRtt(metrics.AverageRTT);
                var stateChangedPacketLoss = rttMonitor.UpdatePacketLoss(metrics.PacketLoss);

                if (stateChangedRtt || stateChangedPacketLoss) {
                    Console.WriteLine(
                        $"[Metrics] Network state change detected between Node {tuple.Item1.Id} and Node {tuple.Item2.Id}");
                }
            }
        }

        private static void UpdateConnectionWeights() {
            foreach (var kvp in rttMonitors) {
                var (node1, node2) = kvp.Key;
                var rttMonitor = kvp.Value;

                var weight = rttMonitor.RttAverage + rttMonitor.PacketLossAverage * 1500;

                networkGraph.UpdateConnectionWeight(node1, node2, (int)weight);
            }
        }
    }
}