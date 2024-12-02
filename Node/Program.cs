using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ESR.Shared;

namespace ESR.Node {
    public struct MetricsPacket {
        private static int s_IdCounter;

        public static int IdCounter => Interlocked.Increment(ref s_IdCounter);

        public DateTime Sent { get; }
        public int Id { get; }

        public MetricsPacket() {
            Sent = DateTime.Now;
            Id = IdCounter % 100;
        }
    }

    public readonly struct MetricsPacketAck {
        public int Id { get; }
        public float RTT { get; }

        public MetricsPacketAck(MetricsPacket packet) {
            Id = packet.Id;
            RTT = (float)(DateTime.Now - packet.Sent).TotalMilliseconds;
        }
    }

    internal static class Program {
        public static List<NodeConnection> s_Connections = new();
        public static ConcurrentDictionary<int, ConcurrentBag<MetricsPacket>> s_Metrics = new();
        public static ConcurrentDictionary<int, ConcurrentBag<MetricsPacketAck>> s_MetricsAck = new();
        public static ConcurrentDictionary<string, List<int>> s_ForwardTo = new(); // contentId -> nodeIds
        public static bool isPop;
        public static ConcurrentDictionary<string, List<TcpClient>> PopTcpClients = new(); // contentId -> clients


        private static void Main() {
            var bootstrap = new Thread(Bootstrap);
            bootstrap.Start();
            try {
                var metrics = new Thread(Metrics);
                metrics.Start();
                var videoStream = new Thread(VideoStream);
                videoStream.Start();
            }
            catch (Exception e) {
                Console.WriteLine($"[Main] Error: {e.Message}");
                Console.WriteLine($"[Main] Stack Trace: {e.StackTrace}");
            }
        }

        private static void Bootstrap() {
            Console.WriteLine("[Bootstrap] Starting bootstrap process...");
            var response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, OpCodes.Bootstrap, false);
            var deserialized = JsonSerializer.Deserialize<NodeResponse>(response.Arguments[0]);
            isPop = deserialized.IsPop;
            s_Connections = deserialized.Connections;
            Console.WriteLine($"[Bootstrap] Is POP: {isPop}");
            Console.WriteLine($"[Bootstrap] Retrieved {s_Connections.Count} connections.");

            if (isPop) {
                Task.Run(PopListener);
            }
            
            while (true) {
                try {
                    response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, false);
                    if (response.OpCode == OpCodes.ForwardTo) {
                        Console.WriteLine("[Bootstrap] ForwardTo update received.");
                        Console.WriteLine(
                            $"[Bootstrap] ForwardTo update received. Content ID: {response.Arguments[0]}, Forwarding to: {string.Join(", ", response.Arguments.Skip(1))}");
                        var contentId = response.Arguments[0];
                        if (!s_ForwardTo.ContainsKey(contentId)) s_ForwardTo[contentId] = new();
                        s_ForwardTo[contentId].Clear();
                        for (int i = 1; i < response.Arguments.Length; i++) {
                            var id = int.Parse(response.Arguments[i]);
                            s_ForwardTo[contentId].Add(id);
                        }
                    }

                    if (response.OpCode != OpCodes.NodeUpdate) continue;
                    Console.WriteLine("[Bootstrap] NodeUpdate received.");
                    var nodeId = int.Parse(response.Arguments[0]);
                    var node = s_Connections.FirstOrDefault(c => c.Id == nodeId);
                    if (node != null) {
                        node.Connected = Encoding.UTF8.GetBytes(response.Arguments[1])[0] != 0;
                        Console.WriteLine($"[Bootstrap] Node {nodeId} updated. Connected: {node.Connected}");
                    }
                }
                catch (Exception e) {
                    Console.WriteLine($"[Bootstrap] Error: {e.Message}");
                }
            }
        }


        private static void Metrics() {
            NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsSender, async client => {
                while (true) {
                    foreach (var con in s_Connections) {
                        if (con.Id == 0 || con.Id == -1 || !con.Connected) continue;

                        Console.WriteLine($"[Node {con.Id}] Sending metrics packets...");

                        s_Metrics[con.Id] = new ConcurrentBag<MetricsPacket>();
                        s_MetricsAck[con.Id] = new ConcurrentBag<MetricsPacketAck>();

                        for (var i = 0; i < 10; i++) {
                            var metricsPacket = new MetricsPacket();
                            s_Metrics[con.Id].Add(metricsPacket);

                            var packet = new PacketBuilder()
                                .WriteOpCode(OpCodes.Metrics)
                                .WriteArgument(metricsPacket.Id.ToString())
                                .Packet;

                            await client.SendAsync(packet, packet.Length, con.Aliases[0],
                                Consts.UdpPortMetricsListener);
                        }

                        _ = Task.Run(() => CalculateMetrics(con));
                    }

                    await Task.Delay(10000);
                }
            });

            NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsListener, async client => {
                while (true) {
                    var result = await client.ReceiveAsync();
                    PacketReader reader = new(result.Buffer);

                    if (reader.GetOpCode(out _).OpCode != OpCodes.Metrics) continue;

                    reader.GetArguments(out var arguments);
                    var packet = new PacketBuilder()
                        .WriteOpCode(OpCodes.MetricsAck)
                        .WriteArgument(arguments[0])
                        .Packet;

                    await client.SendAsync(packet, packet.Length, result.RemoteEndPoint.Address.ToString(),
                        Consts.UdpPortMetricsAckListener);
                }
            });

            NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsAckListener, async client => {
                while (true) {
                    var result = await client.ReceiveAsync();
                    PacketReader reader = new(result.Buffer);
                    var ip = result.RemoteEndPoint.Address.ToString();

                    if (reader.GetOpCode(out _).OpCode != OpCodes.MetricsAck) continue;

                    reader.GetArguments(out var arguments);
                    var id = -1;

                    foreach (var con in s_Connections) {
                        if (con.Aliases.Contains(ip)) {
                            id = con.Id;
                            break;
                        }
                    }

                    if (id == 0) continue;

                    try {
                        if (!s_Metrics.TryGetValue(id, out var sentMetrics)) continue;

                        var ackPacket = sentMetrics.FirstOrDefault(m => m.Id == int.Parse(arguments[0]));
                        if (!ackPacket.Equals(default(MetricsPacket))) {
                            var packetAck = new MetricsPacketAck(ackPacket);
                            s_MetricsAck[id].Add(packetAck);
                        }
                    }
                    catch (Exception e) {
                        Console.WriteLine($"[Node {id}] Error processing ack: {e.Message}");
                    }
                }
            });
        }

        private static async Task CalculateMetrics(NodeConnection node) {
            await Task.Delay(5000);

            var received = 0;
            var totalRtt = 0f;

            foreach (var packet in s_Metrics[node.Id]) {
                var ack = s_MetricsAck[node.Id].FirstOrDefault(a => a.Id == packet.Id);
                if (!ack.Equals(default(MetricsPacketAck))) {
                    received++;
                    totalRtt += ack.RTT;
                }
            }

            var lossRate = 1 - (received / (float)s_Metrics[node.Id].Count);
            var avgRtt = received > 0 ? totalRtt / received : 0;

            Console.WriteLine($"[Node {node.Id}] Metrics - Packet Loss: {lossRate:P}, Avg RTT: {avgRtt}ms");

            var metrics = new Metrics(node, avgRtt, lossRate);

            await NetworkMessenger.SendAsync(Consts.TrackerIpAddress, Consts.TcpPort, OpCodes.Metrics, false,
                JsonSerializer.Serialize(metrics));
            Console.WriteLine("[Metrics] Sending metrics to tracker...");
        }


        private static void VideoStream() {
            Console.WriteLine("[VideoStream] Starting video stream forwarding...");
            NetworkMessenger.StartUdpClient(Consts.UdpPort, async client => {
                while (true) {
                    var result = await client.ReceiveAsync();
                    PacketReader reader = new(result.Buffer);

                    if (reader.GetOpCode(out _).OpCode != OpCodes.VideoStream) continue;

                    Console.WriteLine($"[VideoStream] Received stream from {result.RemoteEndPoint.Address}.");

                    reader.GetArguments(out var args);

                    if (args.Length < 5) {
                        Console.WriteLine("[VideoStream] Invalid packet received.");
                        continue;
                    }

                    var s_ForwardToCopy =
                        s_ForwardTo.ToDictionary(entry => entry.Key, entry => new List<int>(entry.Value));

                    var contentId = args[0];

                    if (s_ForwardToCopy.TryGetValue(contentId, out var destinations)) {
                        foreach (var dest in destinations) {
                            var ipstr = s_Connections.Find(x => x.Id == dest)?.Aliases[0];
                            if (string.IsNullOrEmpty(ipstr)) continue;

                            var ip = IPAddress.Parse(ipstr);
                            var ipEndpoint = new IPEndPoint(ip, Consts.UdpPort);
                            await client.SendAsync(result.Buffer, result.Buffer.Length, ipEndpoint);

                            Console.WriteLine($"[VideoStream] Forwarded fragment to {ip}. {contentId}");
                        }
                    }

                    if (!isPop) continue;
                    var popTcpClientsCopy = PopTcpClients.ToDictionary(entry => entry.Key, entry => new List<TcpClient>(entry.Value));
                    if (popTcpClientsCopy.TryGetValue(contentId, out var tcpClients)) {
                        foreach (var tcpClient in tcpClients) {
                            var tcpClientIp = Utils.GetIPAddressFromTcpClient(tcpClient);
                            var ipEndpoint = new IPEndPoint(IPAddress.Parse(tcpClientIp), Consts.UdpPort);
                            await client.SendAsync(result.Buffer, result.Buffer.Length, ipEndpoint);

                            Console.WriteLine($"[VideoStream] Forwarded fragment to POP client {tcpClientIp}. {contentId}");
                        }
                    }
                    
                    
                }
            });
        }


        private static readonly object _lock = new object();

        private async static void PopListener() {
            var ipEndPoint = new IPEndPoint(IPAddress.Any, Consts.TcpPortPopListener);
            var listener = new TcpListener(ipEndPoint);
            listener.Start();
            Console.WriteLine("[PopListener] Listener started.");

            try {
                while (true) {
                    try {
                        var tcpClient = await listener.AcceptTcpClientAsync();
                        Console.WriteLine("[PopListener] Accepted new client connection.");
                        Task.Run(() => HandlePopConnection(tcpClient));
                    }
                    catch (Exception e) {
                        Console.WriteLine($"[PopListener] Error: {e.Message}");
                    }
                }
            }
            finally {
                listener.Stop();
                Console.WriteLine("[PopListener] Listener stopped.");
            }
        }

        private static void HandlePopConnection(TcpClient tcpClient) {
            Console.WriteLine("[HandlePopConnection] Handling Client Connection");
            try {
                var stream = tcpClient.GetStream();

                while (true) {
                    _ = new PacketReader(stream).GetOpCode(out var opCode).GetArguments(out var args);

                    Console.WriteLine($"[HandlePopConnection] Received OpCode: {opCode:X} - {opCode}");

                    if (opCode == OpCodes.Disconnect) {
                        Console.WriteLine(
                            $"[HandlePopConnection] Client {tcpClient.Client.RemoteEndPoint} disconnected");
                        lock (_lock) {
                            foreach (var key in PopTcpClients.Keys.ToList()) {
                                PopTcpClients[key].Remove(tcpClient);
                                Console.WriteLine($"[HandlePopConnection] Removed client from {key}");
                                if (PopTcpClients[key].Count == 0) {
                                    NetworkMessenger.SendAsync(Consts.TrackerIpAddress, Consts.TcpPort,
                                        OpCodes.StopStreaming, false, key);
                                    Console.WriteLine($"[HandlePopConnection] Sent StopStreaming for {key}");
                                }
                            }
                        }

                        break;
                    }
                    else {
                        switch (opCode) {
                            case OpCodes.StartStreaming:
                                if (args.Length < 1) {
                                    Console.WriteLine("[HandlePopConnection] Invalid packet received.");
                                    break;
                                }

                                var contentId = args[0];
                                lock (_lock) {
                                    if (!PopTcpClients.ContainsKey(contentId)) PopTcpClients[contentId] = new();
                                    PopTcpClients[contentId].Add(tcpClient);
                                    Console.WriteLine($"[HandlePopConnection] Added client to {contentId}");
                                }

                                if (PopTcpClients[contentId].Count == 1) {
                                    NetworkMessenger.SendAsync(Consts.TrackerIpAddress, Consts.TcpPort,
                                        OpCodes.StartStreaming, false, contentId);
                                    Console.WriteLine($"[HandlePopConnection] Sent StartStreaming for {contentId}");
                                }

                                break;

                            case OpCodes.StopStreaming:
                                lock (_lock) {
                                    foreach (var ScontentId in PopTcpClients.Keys.ToList()) {
                                        if (PopTcpClients[ScontentId].Contains(tcpClient)) {
                                            PopTcpClients[ScontentId].Remove(tcpClient);
                                            Console.WriteLine(
                                                $"[HandlePopConnection] Removed client from {ScontentId}");
                                            if (PopTcpClients[ScontentId].Count == 0) {
                                                NetworkMessenger.SendAsync(Consts.TrackerIpAddress, Consts.TcpPort,
                                                    OpCodes.StopStreaming, false, ScontentId);
                                                Console.WriteLine(
                                                    $"[HandlePopConnection] Sent StopStreaming for {ScontentId}");
                                            }
                                        }
                                    }
                                }

                                break;
                            default:
                                Console.WriteLine(
                                    $"[HandlePopConnection] Unknown OpCode: {opCode} from {tcpClient.Client.RemoteEndPoint}");
                                break;
                        }
                    }
                }
            }
            catch (Exception e) {
                Console.WriteLine($"[HandlePopConnection] Error: {e.Message}");
            }
            finally {
                tcpClient.Close();
                Console.WriteLine("[HandlePopConnection] Closed client connection.");
            }
        }
    }
}