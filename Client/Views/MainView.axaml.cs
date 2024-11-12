using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.IO;
using System.Net.Sockets;
using System;
using ESR.Shared;

namespace Client.Views;

public partial class MainView : UserControl
{
    private PacketBuilder m_PacketBuilder = new();
    
    private const int c_Port = 5000;
    private UdpClient m_UdpClient;
    private TcpClient m_TcpClient;
    private bool m_IsRunning = true;
    
    public MainView()
    {
        InitializeComponent();
        AskServerForVideo();
        StartUdpStream();
    }
    
    private async void StartUdpStream()
    {
        m_UdpClient = new UdpClient(Consts.UdpPort + 1);

        try
        {
            while (m_IsRunning)
            {
                var result = await m_UdpClient.ReceiveAsync();
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
        m_UdpClient.Close();
        m_TcpClient.Close();
    }

    private void AskServerForVideo()
    {
        Console.WriteLine("Asking server for video...");
        NetworkMessenger.Send("127.0.0.1", Consts.TcpPort, OpCodes.StartStreaming);
        
        // m_TcpClient = new TcpClient();
        // m_TcpClient.Connect("127.0.0.1", Consts.TcpPort);
        //
        // var stream = m_TcpClient.GetStream();
        // stream.WriteByte((byte)OpCodes.StartStreaming);
    }
}