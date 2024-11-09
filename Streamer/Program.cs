using System.Net;
using System.Net.Sockets;

using ESR.Shared;

namespace ESR.Streamer {
	internal static class Program {
		private static Mutex s_Mut = new();
		
		private static bool s_ShouldSendVideo;
		private static bool p_ShouldSendVideo {
			get {
				s_Mut.WaitOne();
				var value = s_ShouldSendVideo;
				s_Mut.ReleaseMutex();
				return value;
			}
			set {
				s_Mut.WaitOne();
				s_ShouldSendVideo = value;
				s_Mut.ReleaseMutex();
			}
		}
		
		private static IPEndPoint? s_EndPoint;
		private static bool shouldClose;

		private static void Main() {
			var listener = new Thread(Listen);
			var sender = new Thread(SendVideo);

			listener.Start();
			sender.Start();
		}

		private static void SetShouldSendVideo(bool value, IPEndPoint? endPoint = null) {
			s_EndPoint = endPoint;
			p_ShouldSendVideo = value;
		}
		
		private static void Listen() {
			var ipEndPoint = new IPEndPoint(IPAddress.Any, Consts.TcpPort);
			var listener = new TcpListener(ipEndPoint);
			listener.Start();

			while (!shouldClose) {
				try {
					Console.WriteLine("[Listener] Waiting for connection...");
					using var handler = listener.AcceptTcpClient();
					Console.WriteLine($"[Listener] Connected to client {handler.Client.RemoteEndPoint}");
					using var stream = handler.GetStream();

					while (handler.Connected) {
						var opCode = stream.ReadByte();
						if (opCode == -1) {
							Console.WriteLine($"[Listener] Client {handler.Client.RemoteEndPoint} disconnected");
							break;
						}
						
						if ((OpCodes)opCode != OpCodes.None) {
							Console.WriteLine($"[Listener] Received OpCode: {opCode:X} - {(OpCodes)opCode}");
						}

						switch ((OpCodes)opCode) {
							case OpCodes.None:
								break;
							case OpCodes.StartStreaming:
								SetShouldSendVideo(true, handler.Client.RemoteEndPoint as IPEndPoint);
								break;
							case OpCodes.StopStreaming:
								SetShouldSendVideo(false);
								break;
							case OpCodes.Shutdown:
								shouldClose = true;
								return;
							default:
								Console.WriteLine("[Listener] Invalid OpCode Received: " + opCode);
								break;
						}

						Thread.Sleep(50);
					}
				}
				catch (Exception e) {
					Console.Error.WriteLine("Error: " + e.Message);
					Thread.Sleep(Consts.ErrorTimeout);
				}
			}
		}

		private static void SendVideo() {
			var udpClient = new UdpClient(Consts.UdpPort);

			byte[] frameBuffer = File.ReadAllBytes(Consts.MjpegFilePath);
			
			while (!shouldClose) {
				try {
					Console.WriteLine("[Sender] Waiting for start signal...");
					while (p_ShouldSendVideo) {
						if (s_EndPoint == null) {
							throw new InvalidOperationException("No end point specified for sending video");
						}

						s_EndPoint.Port = Consts.UdpPort + 1;
						Console.WriteLine($"[Sender] Sending frame to endpoint {s_EndPoint}...");
						udpClient.Send(frameBuffer, frameBuffer.Length, s_EndPoint);

						Thread.Sleep(1000 / Consts.FrameRate);
					}
					Thread.Sleep(Consts.Timeout);
				}
				catch (Exception e) {
					Console.Error.WriteLine("Error: " + e.Message);
					Thread.Sleep(Consts.ErrorTimeout);
				}
			}

			udpClient.Close();
		}
	}
}

/*

		public void Start() {
			try {
				byte[] mjpegData = File.ReadAllBytes(MjpegFilePath); // Read the entire MJPEG file into memory
				int currentPosition = 0;

				while (true) {
					try {
						// Find the start of the next JPEG frame (0xFF, 0xD8)
						int frameStart = FindFrameStart(mjpegData, currentPosition);
						if (frameStart == -1) break; // No more frames

						// Find the end of the JPEG frame (0xFF, 0xD9)
						int frameEnd = FindFrameEnd(mjpegData, frameStart);
						if (frameEnd == -1) break; // No valid frame end

						// Extract the JPEG frame
						byte[] frame = new byte[frameEnd - frameStart + 1];
						Array.Copy(mjpegData, frameStart, frame, 0, frame.Length);

						// Send the frame over UDP
						_udpClient.Send(frame, frame.Length, _endPoint);

						// Update the position for the next frame
						currentPosition = frameEnd + 1;

						if (currentPosition >= mjpegData.Length) currentPosition = 0;

						Thread.Sleep(1000 / FrameRate);
					} catch (Exception ex) {
						Console.WriteLine($"Error parsing MJPEG stream: {ex.Message}");
						break;
					}
				}

				Console.WriteLine("MJPEG stream sent successfully.");
			} catch (Exception ex) {
				Console.WriteLine($"Error reading MJPEG file: {ex.Message}");
			}
		}

		private int FindFrameStart(byte[] data, int start) {
			// JPEG frame starts with 0xFF, 0xD8
			for (int i = start; i < data.Length - 1; i++) {
				if (data[i] == 0xFF && data[i + 1] == 0xD8) {
					return i;
				}
			}
			return -1;
		}

		private int FindFrameEnd(byte[] data, int start) {
			// JPEG frame ends with 0xFF, 0xD9
			for (int i = start; i < data.Length - 1; i++) {
				if (data[i] == 0xFF && data[i + 1] == 0xD9) {
					return i + 1;
				}
			}
			return -1;
		}

		public void Stop() {
			_udpClient.Close();
		}

		static void Main(string[] args) {
			MjpegUdpServer server = new MjpegUdpServer();
			server.Start();

			// Keep the server running
			Console.WriteLine("Press any key to stop the server...");
			Console.ReadKey();
			server.Stop();
		}
	}

}```
 *
 * 
*/