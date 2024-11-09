using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.IO;
using System.Net.Sockets;
using System;
using ESR.Shared;

namespace Client.Views;

public partial class MainView : UserControl
{
    private const int c_Port = 5000;
    private UdpClient _udpClient;
    private TcpClient _tcpClient;
    private bool _isRunning = true;
    
    public MainView()
    {
        InitializeComponent();
        AskServerForVideo();
        StartUdpStream();
    }
    
    private async void StartUdpStream()
    {
        _udpClient = new UdpClient(Consts.UdpPort + 1);

        try
        {
            while (_isRunning)
            {
                var result = await _udpClient.ReceiveAsync();
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
        _isRunning = false;
        _udpClient.Close();
        _tcpClient.Close();
    }

    private void AskServerForVideo()
    {
        Console.WriteLine("Asking server for video...");
        _tcpClient = new TcpClient();
        _tcpClient.Connect("127.0.0.1", Consts.TcpPort);

        var stream = _tcpClient.GetStream();
        stream.WriteByte(0x01);
    }
}