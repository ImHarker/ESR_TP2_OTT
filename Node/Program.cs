using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ESR.Shared;

namespace ESR.Node
{
    internal static class Program
    {
        public static List<string> Connections = [];

        private static void Main()
        {
            Console.WriteLine("[Tracker] Asking Tracker For Nearby Nodes...");

            Bootstrap();

            if (Connections.Count == 0)
            {
                throw new Exception("[Tracker] No nodes found.");
            }
            else
            {
                Console.WriteLine("[Tracker] Found nodes:");
                foreach (var connection in Connections)
                {
                    Console.WriteLine(connection);
                }

                NetworkMessenger.DisposeTcpClient(Consts.TrackerIpAddress);
            }

            var heartBeatSender = new Thread(HeartBeatSender);
            heartBeatSender.Start();
            var heartBeatListener = new Thread(HeartBeatListener);
            heartBeatListener.Start();
        }

        private static void Bootstrap()
        {
            var response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, OpCodes.GetNodes, false);
            var args = response.Arguments;
            List<NodeResponse> nodeResponse = [];
            foreach (var arg in args)
            {
                nodeResponse.Add(JsonSerializer.Deserialize<NodeResponse>(arg));
            }
            
            foreach (var node in nodeResponse)
            {
                Connections.AddRange(node.Connections);
            }
        }

        private static void HeartBeatSender()
        {
            NetworkMessenger.StartUdpClient(Consts.UdpPortHeartbeat, async client =>
            {
                foreach (var con in Connections)
                {
                    var packet = new PacketBuilder().WriteOpCode(OpCodes.Heartbeat).Packet;
                    await client.SendAsync(packet, packet.Length, con, Consts.UdpPortHeartbeatResponse);
                }

                Thread.Sleep(5000);
            });
        }

        private static void HeartBeatListener()
        {
            NetworkMessenger.StartUdpClient(Consts.UdpPortHeartbeatResponse, async client =>
            {
                var result = await client.ReceiveAsync();
                var ip = result.RemoteEndPoint.Address.ToString();
                Console.WriteLine($"[Heartbeat] Received heartbeat from {ip}");
            });
        }
    }
}