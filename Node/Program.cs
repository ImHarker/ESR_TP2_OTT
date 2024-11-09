using System.Net.Sockets;
using System.Text;
using ESR.Shared;

namespace ESR.Node
{
	internal static class Program
	{
		private static void Main()
		{
			Console.WriteLine("Asking Tracker For Nearby Nodes...");
			var _tcpClient = new TcpClient();
			_tcpClient.Connect("127.0.0.1", Consts.TcpPort);

			var stream = _tcpClient.GetStream();
			stream.WriteByte(0x03);
			
			while (true)
			{
				if (stream.DataAvailable)
				{
					var buffer = new byte[1024];
					_ = stream.Read(buffer, 0, buffer.Length);
					Console.WriteLine(Encoding.UTF8.GetString(buffer));
				}
			}
		}
	}
}
