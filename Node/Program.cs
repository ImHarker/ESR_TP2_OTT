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

    internal static class Program
    {
        public static List<NodeConnection> s_Connections = [];
        public static Dictionary<int, List<MetricsPacket>> s_Metrics = new();

        private static void Main()
        {
            var bootstrap = new Thread(Bootstrap);
            bootstrap.Start();
            var metrics = new Thread(Metrics);
            metrics.Start();
        }

        private static void Bootstrap()
        {
            var response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, OpCodes.Bootstrap, false);
            Console.WriteLine(response.ToString());
            s_Connections = JsonSerializer.Deserialize<NodeResponse>(response.Arguments[0]).Connections;
            
            while (true)
            {
                try
                {
                    response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, false);
                    
                    if (response.OpCode != OpCodes.NodeUpdate) continue;
                    Console.WriteLine($"[Node] Received Node Update. Node {response.Arguments[0]} {(Encoding.UTF8.GetBytes(response.Arguments[1])[0] == 0 ? "disconnected" : "connected")}");
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
                        for (var i = 0; i < 10; i++)
                        {
                            s_Metrics[con.Id].Add(new MetricsPacket());
                            var packetId = Encoding.UTF8.GetString(new[] { s_Metrics[con.Id][^1].Id });
                            var packet = new PacketBuilder().WriteOpCode(OpCodes.Metrics).WriteArgument(packetId).Packet;
                            await client.SendAsync(packet, packet.Length, con.Aliases[0], Consts.UdpPortMetricsListener);
                            Console.WriteLine($"[Node] Sent Metrics to {con.Id} - {s_Metrics[con.Id][^1].Id}");
                        }
                    }

                    Thread.Sleep(10000);
                }
            });

            NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsListener, async client =>
            {
                while (true)
                {
                    var result = await client.ReceiveAsync();
                    PacketReader reader = new(result.Buffer);
                    var ip = result.RemoteEndPoint.Address.ToString();

                    if (reader.GetOpCode(out _).OpCode != OpCodes.Metrics) continue;
                    reader.GetArguments(out var arguments);
                    Console.WriteLine($"[Node] Received Metrics from {ip} - {Encoding.UTF8.GetBytes(arguments[0])[0]}");
                    var packet = new PacketBuilder().WriteOpCode(OpCodes.MetricsAck).WriteArgument(arguments[0]).Packet;
                    await client.SendAsync(packet, packet.Length, result.RemoteEndPoint.Address.ToString(), Consts.UdpPortMetricsAckListener);
                    Console.WriteLine($"[Node] Sent Metrics Ack to {ip} - {Encoding.UTF8.GetBytes(arguments[0])[0]}");
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
                    Console.WriteLine($"[Node] Received Metrics Ack from {ip}");
                    reader.GetArguments(out var arguments);

                    int id = -1;
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

                    if (!s_Metrics.ContainsKey(id)) continue;
                    var rtt = (DateTime.Now - s_Metrics[id].First(x => x.Id == Encoding.UTF8.GetBytes(arguments[0])[0]).Sent).TotalMilliseconds;
                    Console.WriteLine($"[Node] RTT from {id} is {rtt}ms");
                }
            });
        }
    }
}