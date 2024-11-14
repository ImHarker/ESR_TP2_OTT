using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.IO;
using System.Net.Sockets;
using System;
using System.Threading.Tasks;
using ESR.Shared;

namespace Client.Views;

public partial class MainView : UserControl
{
    private bool m_IsRunning = true;

    public MainView()
    {
        InitializeComponent();
        AskServerForVideo();
        StartUdpStream();
    }

    private void StartUdpStream()
    {
        Console.WriteLine("A");
        NetworkMessenger.StartUdpClient(Consts.UdpPort + 1, async client =>
        {
            try
            {
                while (m_IsRunning)
                {
                    var result = await client.ReceiveAsync();
                    var frameBuffer = result.Buffer;

                    DisplayFrame(frameBuffer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving UDP stream: {ex.Message}");
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
        NetworkMessenger.DisposeTcpClient(Consts.StreamerIpAddress);
    }

    private static void AskServerForVideo()
    {
        NetworkMessenger.Send(Consts.StreamerIpAddress, Consts.TcpPort, OpCodes.StartStreaming, false);
    }
}