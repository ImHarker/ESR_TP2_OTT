using System.Net;
using System.Net.Sockets;
using ESR.Shared;

namespace ESR.Streamer
{
    internal static class Program
    {
        private static Mutex s_Mut = new();

        private static bool s_ShouldSendVideo;

        private static bool p_ShouldSendVideo
        {
            get
            {
                s_Mut.WaitOne();
                var value = s_ShouldSendVideo;
                s_Mut.ReleaseMutex();
                return value;
            }
            set
            {
                s_Mut.WaitOne();
                s_ShouldSendVideo = value;
                s_Mut.ReleaseMutex();
            }
        }

        private static IPEndPoint? s_EndPoint;
        private static bool shouldClose;

        private static void Main()
        {
            var listener = new Thread(Listen);
            var sender = new Thread(SendVideo);

            listener.Start();
            sender.Start();
        }

        private static void SetShouldSendVideo(bool value, IPEndPoint? endPoint = null)
        {
            s_EndPoint = endPoint;
            p_ShouldSendVideo = value;
        }

        private static void Listen()
        {
            var ipEndPoint = new IPEndPoint(IPAddress.Any, Consts.TcpPort);
            var listener = new TcpListener(ipEndPoint);
            listener.Start();

            while (!shouldClose)
            {
                try
                {
                    Console.WriteLine("[Listener] Waiting for connection...");
                    using var handler = listener.AcceptTcpClient();
                    Console.WriteLine($"[Listener] Connected to client {handler.Client.RemoteEndPoint}");
                    using var stream = handler.GetStream();

                    while (handler.Connected)
                    {
                        _ = new PacketReader(stream).GetOpCode(out var opCode).GetArguments(out _);
                        
                        if (opCode == OpCodes.Disconnect)
                        {
                            Console.WriteLine($"[Listener] Client {handler.Client.RemoteEndPoint} disconnected");
                            SetShouldSendVideo(false);
                            break;
                        }

                        if ((OpCodes)opCode != OpCodes.None)
                        {
                            Console.WriteLine($"[Listener] Received OpCode: {opCode:X} - {opCode}");
                        }

                        switch ((OpCodes)opCode)
                        {
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
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("[Listener-Error] " + e.Message);
                    Thread.Sleep(Consts.ErrorTimeout);
                }
            }
        }

        private static void SendVideo()
        {
            var udpClient = new UdpClient(Consts.UdpPort);

            byte[] frameBuffer = File.ReadAllBytes(Consts.MjpegFilePath);

            while (!shouldClose)
            {
                try
                {
                    Console.WriteLine("[Sender] Waiting for start signal...");
                    while (p_ShouldSendVideo)
                    {
                        if (s_EndPoint == null)
                        {
                            throw new InvalidOperationException("No end point specified for sending video");
                        }

                        s_EndPoint.Port = Consts.UdpPort + 1;
                        udpClient.Send(frameBuffer, frameBuffer.Length, s_EndPoint);

                        Thread.Sleep(1000 / Consts.FrameRate);
                    }
                    
                    Thread.Sleep(Consts.Timeout);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Error: " + e.Message);
                    Thread.Sleep(Consts.ErrorTimeout);
                }
            }

            udpClient.Close();
        }
    }
}