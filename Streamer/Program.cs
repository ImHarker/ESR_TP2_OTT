using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ESR.Shared;

namespace ESR.Streamer {
    internal static class Program {
        private static bool shouldClose;

        private static ConcurrentDictionary<string, List<int>> s_StreamingContent = new();
        public static List<NodeConnection> s_Connections = new();


        private static void Main() {
            Directory.CreateDirectory(Consts.StreamingDirectory);

            var bootstrap = new Thread(Bootstrap);
            var listener = new Thread(Listen);
            var sender = new Thread(SendVideo);
            
            bootstrap.Start();
            listener.Start();
            sender.Start();
            
            // Console.WriteLine("[Main] Sender thread started.");
            //
            // // Keep the main thread alive to allow other threads to run
            // while (!shouldClose) {
            //     Thread.Sleep(1000 * 60 * 2);
            // }
            //
            // Console.WriteLine("[Main] Application is closing.");
        }

        private static void Bootstrap() {
            Console.WriteLine("[Bootstrap] Starting bootstrap process...");
            var response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, OpCodes.Bootstrap, false);
            s_Connections = JsonSerializer.Deserialize<NodeResponse>(response.Arguments[0]).Connections;
            Console.WriteLine($"[Bootstrap] Retrieved {s_Connections.Count} connections.");

            while (true) {
                try {
                    response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, false);
                    if (response.OpCode == OpCodes.ForwardTo) {
                        Console.WriteLine("[Bootstrap] ForwardTo update received.");
                        var contentId = response.Arguments[0];
                        if (!s_StreamingContent.ContainsKey(contentId)) s_StreamingContent[contentId] = new();
                        s_StreamingContent[contentId].Clear();
                        for (int i = 1; i < response.Arguments.Length; i++) {
                            var id = int.Parse(response.Arguments[i]);
                            s_StreamingContent[contentId].Add(id);
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
                        _ = new PacketReader(stream).GetOpCode(out var opCode).GetArguments(out var args);

                        if (opCode == OpCodes.Disconnect) {
                            Console.WriteLine($"[Listener] Client {handler.Client.RemoteEndPoint} disconnected");
                            break;
                        }

                        if (opCode != OpCodes.None) {
                            Console.WriteLine($"[Listener] Received OpCode: {opCode:X} - {opCode}");
                        }

                        switch (opCode) {
                            case OpCodes.None:
                                break;
                            case OpCodes.StartStreaming:

                                break;
                            case OpCodes.StopStreaming:
                                break;
                            case OpCodes.Shutdown:
                                shouldClose = true;
                                return;
                            default:
                                Console.WriteLine("[Listener] Invalid OpCode Received: " + opCode);
                                break;
                        }
                    }
                }
                catch (Exception e) {
                    Console.Error.WriteLine("[Listener-Error] " + e.Message);
                    Thread.Sleep(Consts.ErrorTimeout);
                }
            }
        }

        private async static void SendVideo() {
            var udpClient = new UdpClient(Consts.UdpPort);

            var filesToStream = Directory.EnumerateFiles(Consts.StreamingDirectory).Select(Path.GetFileName).ToArray();
            Console.WriteLine($"[VideoStream] Found {filesToStream.Length} files to stream.");

            try {
                var tasks = filesToStream.Select(path => Task.Run(() => StreamVideo(path, udpClient))).ToArray();
                await Task.WhenAll(tasks);
            }
            catch (Exception e) {
                Console.Error.WriteLine("Error: " + e.Message);
                Thread.Sleep(Consts.ErrorTimeout);
            }
            finally {
                udpClient.Close();
            }
        }

        private async static void StreamVideo(string fileName, UdpClient _udpClient) {
    try {
        Console.WriteLine($"[VideoStream] Streaming {fileName}...");
        var contentId = Path.GetFileNameWithoutExtension(fileName);
        if (!s_StreamingContent.ContainsKey(contentId)) s_StreamingContent[contentId] = new();
        await NetworkMessenger.SendAsync(Consts.TrackerIpAddress, Consts.TcpPort, OpCodes.ContentMetadata, false, contentId);
        
        byte[] mjpegData = File.ReadAllBytes(Path.Join(Consts.StreamingDirectory, fileName));
        int currentPosition = 0;
        int frameNumber = 0; 
        
        while (true) {
            try {
                int frameStart = FindFrameStart(mjpegData, currentPosition);
                if (frameStart == -1) break;

                int frameEnd = FindFrameEnd(mjpegData, frameStart);
                if (frameEnd == -1) break;

                byte[] frame = new byte[frameEnd - frameStart + 1];
                Array.Copy(mjpegData, frameStart, frame, 0, frame.Length);

                
                var videoPackets = VideoPacket.BuildVideoPackets(contentId, frameNumber, frame);

                var destinations = s_StreamingContent[contentId].ToList();
                foreach (var dest in destinations) {
                    var ipstr = s_Connections.Find(x => x.Id == dest)?.Aliases[0];
                    if (string.IsNullOrEmpty(ipstr)) continue;

                    var ip = IPAddress.Parse(ipstr);
                    var ipEndpoint = new IPEndPoint(ip, Consts.UdpPort);

                    foreach (var packet in videoPackets) {
                        if (packet.Length > 1500) {
                            Console.WriteLine($"[VideoStream] Frame too large to send over UDP. {packet.Length} bytes.");
                            continue;
                        }

                        await _udpClient.SendAsync(packet, packet.Length, ipEndpoint);
                        Console.WriteLine($"[VideoStream] Sent frame {frameNumber} to {ip}.");
                    }
                }
                
                //DEBUG
                // var _endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), Consts.UdpPort + 1);
                // foreach (var packet in videoPackets) {
                //     if (packet.Length > 1500) {
                //         Console.WriteLine($"[VideoStream] Frame too large to send over UDP. {packet.Length} bytes.");
                //         continue;
                //     }
                //
                //     await _udpClient.SendAsync(packet, packet.Length, _endPoint);
                //     Console.WriteLine($"[VideoStream] Sent frame {frameNumber} to {_endPoint}. {packet.Length} bytes.");
                // }                
                //DEBUG END

                currentPosition = frameEnd + 1;
                if (currentPosition >= mjpegData.Length) currentPosition = 0;

                frameNumber++;

                Thread.Sleep(1000 / Consts.FrameRate);
            }
            catch (Exception ex) {
                Console.WriteLine($"Error parsing MJPEG stream: {ex.Message}");
                break;
            }
        }
    }
    catch (Exception ex) {
        Console.WriteLine($"Error reading MJPEG file: {ex.Message}");
    }
}


        private static int FindFrameStart(byte[] data, int start) {
            // JPEG frame starts with 0xFF, 0xD8
            for (int i = start; i < data.Length - 1; i++) {
                if (data[i] == 0xFF && data[i + 1] == 0xD8) {
                    return i;
                }
            }

            return -1;
        }

        private static int FindFrameEnd(byte[] data, int start) {
            // JPEG frame ends with 0xFF, 0xD9
            for (int i = start; i < data.Length - 1; i++) {
                if (data[i] == 0xFF && data[i + 1] == 0xD9) {
                    return i + 1;
                }
            }

            return -1;
        }
    }
}