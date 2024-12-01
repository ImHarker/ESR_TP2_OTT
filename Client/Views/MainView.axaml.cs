using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.IO;
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ESR.Shared;

namespace Client.Views;

public partial class MainView : UserControl
{
    private bool m_IsRunning = true;

    public MainView()
    {
        InitializeComponent();
        StartUdpStream();
    }

    private void StartUdpStream()
    {
        NetworkMessenger.StartUdpClient(Consts.UdpPort + 1, async client =>
        {
            try
            {
                while (m_IsRunning)
                {
                    var receivedPackets = new Dictionary<string, List<byte[]>>();
                    var result = await client.ReceiveAsync();
                    PacketReader reader = new(result.Buffer);

                    if (reader.GetOpCode(out _).OpCode != OpCodes.VideoStream) continue;

                    reader.GetArguments(out var args);

                    if (args.Length < 5)
                    {
                        Console.WriteLine("[VideoStream] Invalid packet received.");
                        continue;
                    }

                    var contentId = args[0];
                    byte[] frameData = VideoPacket.ReadVideoPackets(contentId, result.Buffer);

                    if (frameData.Length > 0) {
                        Dispatcher.UIThread.Post(() => DisplayFrame(frameData));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving UDP stream: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Close();
            }
        });
    }

    private void DisplayFrame(byte[] frameBuffer)
    {
        using var stream = new MemoryStream(frameBuffer);
        var bitmap = new Bitmap(stream);
        StreamImage.Source = bitmap;
    }

    public void Close()
    {
        m_IsRunning = false;
        NetworkMessenger.DisposeUdpClient(Consts.UdpPort + 1);
    }
    
}