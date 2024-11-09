using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.IO;
using System.Net.Sockets;
using System;
using System.Threading;
using ESR.Shared;

namespace Client.Views;

public partial class MainView : UserControl {
	private const int c_Port = 5000;
	private UdpClient _udpClient;
	private bool _isRunning = true;

	public MainView() {
		InitializeComponent();
		AskServerForVideo();
		StartUdpStream();
	}

	~MainView()
	{
		StopUdpStream();
	}

	private async void StartUdpStream() {
		_udpClient = new UdpClient(Consts.UdpPort + 1);

		try {
			while (_isRunning) {
				Console.WriteLine("Receiving UDP stream...");
				var result = await _udpClient.ReceiveAsync();
				var frameBuffer = result.Buffer;

				DisplayFrame(frameBuffer);
			}
		} catch (Exception ex) {
			Console.WriteLine($"Error receiving UDP stream: {ex.Message}");
		} finally {
			_udpClient.Close();
		}
	}

	private void DisplayFrame(byte[] frameBuffer) {
		using var stream = new MemoryStream(frameBuffer);

		var bitmap = new Bitmap(stream);
		StreamImage.Source = bitmap;
	}

	public void StopUdpStream() {
		_isRunning = false;
		_udpClient.Close();
	}

	private void AskServerForVideo() {
		Console.WriteLine($"Asking server for video...");
		var client = new TcpClient();
		client.Connect("127.0.0.1", Consts.TcpPort);
		
		var stream = client.GetStream();
		stream.WriteByte(0x01);

		client.Close();
	}
}
