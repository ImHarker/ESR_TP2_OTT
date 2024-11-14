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
			foreach(var arg in args)
			{
				nodeResponse.Add(JsonSerializer.Deserialize<NodeResponse>(arg));
			}
			foreach(var node in nodeResponse)
			{
				Connections.AddRange(node.Connections);
			}
		}

		private static void HeartBeatSender()
		{
			var udpClient = new UdpClient(Consts.UdpPortHeartbeat);
			
			
		}
		
		private static void HeartBeatListener()
		{
			var udpClient = new UdpClient(Consts.UdpPortHeartbeatResponse);
			
			
		}
	}
}
