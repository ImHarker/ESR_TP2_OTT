using System.Text;
using System.Text.Json;
using ESR.Shared;

namespace ESR.Node
{
    public struct MetricsPacket()
    {
        public static byte s_IdCounter;

        public static byte IdCounter
        {
            get => s_IdCounter;
            set => s_IdCounter = value >= 100 ? (byte)0 : value;
        }

        public DateTime Sent { get; } = DateTime.Now;
        public byte Id { get; } = IdCounter++;
    }
    
    public readonly struct MetricsPacketAck(MetricsPacket packet)
    {
        public byte Id { get; } = packet.Id;
        public float RTT { get; } = (float)(DateTime.Now - packet.Sent).TotalMilliseconds;
    }

    public readonly struct Metrics(float averageRTT, float packetLoss)
    {
        public float AverageRTT { get; } = averageRTT;
        public float PacketLoss { get; } = packetLoss;
    }

    internal static class Program
    {
        public static List<NodeConnection> s_Connections = [];
        public static Dictionary<int, List<MetricsPacket>> s_Metrics = new();
        public static Dictionary<int, List<MetricsPacketAck>> s_MetricsAck = new();
        public static Dictionary<int, List<Metrics>> s_MetricsCalc = [];
        
        private static void Main()
        {
            var bootstrap = new Thread(Bootstrap);
            bootstrap.Start();
            try
            {
                var metrics = new Thread(Metrics);
                metrics.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Main] Error: {e.Message}");
                Console.WriteLine($"[Main] Stack Trace: {e.StackTrace}");
            }
        }

        private static void Bootstrap()
        {
            var response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, OpCodes.Bootstrap, false);
            s_Connections = JsonSerializer.Deserialize<NodeResponse>(response.Arguments[0]).Connections;

            foreach (var con in s_Connections)
            {
                s_Metrics.Add(con.Id, []);
                s_MetricsAck.Add(con.Id, []);
                s_MetricsCalc.Add(con.Id, []);
            }

            while (true)
            {
                try
                {
                    response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, false);

                    if (response.OpCode != OpCodes.NodeUpdate) continue;
                    NodeConnection? nodeCon = null;
                    var i = 0;
                    for (; i < s_Connections.Count; i++)
                    {
                        var con = s_Connections[i];
                        if (con.Id == int.Parse(response.Arguments[0]))
                        {
                            nodeCon = con;
                            break;
                        }
                    }

                    if (nodeCon == null) continue;
                    nodeCon.Connected = Encoding.UTF8.GetBytes(response.Arguments[1])[0] != 0;
                    s_Connections[i] = nodeCon;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Node] Error: {e.Message}");
                }
            }
        }

        private static void Metrics()
        {
            NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsSender, async client =>
            {
                while (true)
                {
                    foreach (var con in s_Connections)
                    {
                        if (!con.Connected) continue;
                        
                        var start = s_Metrics[con.Id].Count == 0 ? 0 : s_Metrics[con.Id][^1].Id;
                        
                        s_Metrics[con.Id].Clear();
                        s_MetricsAck[con.Id].Clear();
                        
                        for (var i = 0; i < 10; i++)
                        {
                            s_Metrics[con.Id].Add(new MetricsPacket());
                            var packet = new PacketBuilder()
                                .WriteOpCode(OpCodes.Metrics)
                                .WriteArgument(s_Metrics[con.Id][^1].Id.ToString())
                                .Packet;
                            
                            await client.SendAsync(packet, packet.Length, con.Aliases[0],
                                Consts.UdpPortMetricsListener);
                        }

                        _ = Task.Run(() => CalculateMetrics(con.Id, start));
                    }

                    await Task.Delay(10000);
                }
            });

            NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsListener, async client =>
            {
                while (true)
                {
                    var result = await client.ReceiveAsync();
                    PacketReader reader = new(result.Buffer);

                    if (reader.GetOpCode(out _).OpCode != OpCodes.Metrics) continue;
                    reader.GetArguments(out var arguments);
                    var packet = new PacketBuilder().WriteOpCode(OpCodes.MetricsAck).WriteArgument(arguments[0]).Packet;
                    await client.SendAsync(packet, packet.Length, result.RemoteEndPoint.Address.ToString(),
                        Consts.UdpPortMetricsAckListener);
                }
            });

            NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsAckListener, async client =>
            {
                while (true)
                {
                    var result = await client.ReceiveAsync();
                    PacketReader reader = new(result.Buffer);
                    var ip = result.RemoteEndPoint.Address.ToString();

                    if (reader.GetOpCode(out _).OpCode != OpCodes.MetricsAck) continue;
                    reader.GetArguments(out var arguments);

                    var id = -1;
                    foreach (var con in s_Connections)
                    {
                        for (var i = 0; i < con.Aliases.Length; i++)
                        {
                            var alias = con.Aliases[i];
                            if (alias == ip)
                            {
                                id = con.Id;
                                break;
                            }
                        }
                    }

                    try
                    {
                        if (!s_Metrics.TryGetValue(id, out var metric)) continue;
                        var packet = new MetricsPacketAck(metric.First(x => x.Id == int.Parse(arguments[0])));
                        s_MetricsAck[id].Add(packet);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"[Node] Error: {e.Message}");
                    }
                }
            });
        }

        private static async Task CalculateMetrics(int nodeId, int startId)
        {
            await Task.Delay(5000);
            
            var endId = startId + 10;
            
            var received = 0;
            var avgRtt = 0f;
            for (var i = startId; i < endId; i++)
            {
                var id = i >= 100 ? i - 100 : i;
                
                if (s_MetricsAck.ContainsKey(nodeId) && s_MetricsAck[nodeId].Any(x => x.Id == id))
                {
                    received++;
                    avgRtt += s_MetricsAck[nodeId].First(x => x.Id == id).RTT;
                    
                    Console.WriteLine($"[Node] Received packet {id} from {nodeId} with RTT of {s_MetricsAck[nodeId].First(x => x.Id == id).RTT}ms");
                }
            }
            
            if (received == 0) return;
            Console.WriteLine($"[Node] Received {received / 10f * 100}% packets from {nodeId} with an average RTT of {avgRtt / received}ms");
            s_MetricsCalc[nodeId].Add(new Metrics(avgRtt / received, 1 - received / 10f));
            if (s_MetricsCalc[nodeId].Count > 20) s_MetricsCalc[nodeId].RemoveAt(0);
        }
    }
}