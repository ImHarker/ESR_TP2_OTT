using System.Net.Sockets;
using System.Text;
using ESR.Shared;

namespace ESR.Node
{
	internal static class Program
	{
		public static List<string> Connections = [];
		
		private static async Task Main()
		{
			Console.WriteLine("[Tracker] Asking Tracker For Nearby Nodes...");

			await Bootstrap();

			if (Connections.Count == 0)
			{
				throw new Exception("[Tracker] No nodes found.");
			}
			
			var heartBeatSender = new Thread(HeartBeatSender);
			heartBeatSender.Start();
			var heartBeatListener = new Thread(HeartBeatListener);
			heartBeatListener.Start();
		}

		private static async Task Bootstrap()
		{
			var tcpClient = new TcpClient();
			await tcpClient.ConnectAsync(Consts.TrackerIpAddress, Consts.TcpPort);
			
			var stream = tcpClient.GetStream();
			stream.WriteByte((byte)OpCodes.GetNodes);
			
			var start = Time.Now;
			
			while (tcpClient.Connected)
			{
				if (Time.Now - start > TimeSpan.FromMilliseconds(Consts.Timeout))
				{
					tcpClient.Close();
					throw new Exception("[Tracker] Connection to bootstrapper timed out.");
				}
				if (!stream.DataAvailable) continue;
				
				var buffer = new byte[1024];
				_ = stream.Read(buffer, 0, buffer.Length);
				Console.WriteLine(Encoding.UTF8.GetString(buffer));
				break;
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
