using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DynamicData;
using ESR.Node;
using ESR.Shared;

namespace Client.Views;

public partial class MainView : UserControl {
    public static ConcurrentDictionary<int, ConcurrentBag<MetricsPacket>> s_Metrics = new();
    public static ConcurrentDictionary<int, ConcurrentBag<MetricsPacketAck>> s_MetricsAck = new();
    public static ObservableCollection<string[]> s_Pops = new();
    public static ObservableCollection<string> s_ContentIds = new();
    private bool isUpdatingComboBox = false;
  

    public static Dictionary<int, Metrics> s_MetricsData = new();
    private bool m_IsRunning = true;

    public MainView() {
        InitializeComponent();
        
        PopsComboBox.ItemsSource = s_Pops;
        ContentIdComboBox.ItemsSource = s_ContentIds;
        
        if (Environment.GetEnvironmentVariable("PREVIEW_MODE") == "true") return;
        InitClient();
        
        
        var metrics = new Thread(Metrics);
        var udp = new Thread(StartUdpStream);
        udp.Start();
        metrics.Start();
        
    }

    private void InitClient() {
        var response = NetworkMessenger.Get(Consts.TrackerIpAddress, Consts.TcpPort, OpCodes.ContentMetadata, true);

        var metadata = JsonSerializer.Deserialize<ContentMetadata>(response.Arguments[0]);
        Console.WriteLine(
            $"[MainView] Received metadata: {metadata.ContentIds.Count} content IDs, {metadata.Pops.Count} POPs.");
        
        UpdatePops(metadata.Pops);
        UpdateContentIds(metadata.ContentIds);
        
    }


    private static void Metrics() {
        Console.WriteLine("[Metrics] Starting metrics thread...");
        NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsSender, async client => {
            while (true) {
                var s_PopsCopy = new List<string[]>(s_Pops);
                foreach (var pop in s_PopsCopy) {
                    if (pop[0] == "None") continue;
                    Console.WriteLine($"[Metrics] Sending metrics to POP {pop[0]}...");
                    var id = Utils.IpToInt32(pop[0]);
                    s_Metrics[id] = new ConcurrentBag<MetricsPacket>();
                    s_MetricsAck[id] = new ConcurrentBag<MetricsPacketAck>();

                    for (var i = 0; i < 10; i++) {
                        var metricsPacket = new MetricsPacket();
                        s_Metrics[id].Add(metricsPacket);

                        var packet = new PacketBuilder()
                            .WriteOpCode(OpCodes.Metrics)
                            .WriteArgument(metricsPacket.Id.ToString())
                            .Packet;

                        await client.SendAsync(packet, packet.Length, pop[0],
                            Consts.UdpPortMetricsListener);
                    }

                    _ = Task.Run(() => CalculateMetrics(id));
                }

                await Task.Delay(10000);
            }
        });


        NetworkMessenger.StartUdpClient(Consts.UdpPortMetricsAckListener, async client => {
            while (true) {
                var result = await client.ReceiveAsync();
                PacketReader reader = new(result.Buffer);
                var ip = result.RemoteEndPoint.Address.ToString();

                if (reader.GetOpCode(out _).OpCode != OpCodes.MetricsAck) continue;

                reader.GetArguments(out var arguments);
                var id = -1;

                foreach (var pop in s_Pops) {
                    if (pop.Contains(ip)) {
                        id = Utils.IpToInt32(pop[0]);
                        break;
                    }
                }

                try {
                    if (!s_Metrics.TryGetValue(id, out var sentMetrics)) continue;

                    var ackPacket = sentMetrics.FirstOrDefault(m => m.Id == int.Parse(arguments[0]));
                    if (!ackPacket.Equals(default(MetricsPacket))) {
                        var packetAck = new MetricsPacketAck(ackPacket);
                        s_MetricsAck[id].Add(packetAck);
                    }
                }
                catch (Exception e) {
                    Console.WriteLine($"[Node {id}] Error processing ack: {e.Message}");
                }
            }
        });
    }

    private static async Task CalculateMetrics(int id) {
        await Task.Delay(5000);

        var received = 0;
        var totalRtt = 0f;

        foreach (var packet in s_Metrics[id]) {
            var ack = s_MetricsAck[id].FirstOrDefault(a => a.Id == packet.Id);
            if (!ack.Equals(default(MetricsPacketAck))) {
                received++;
                totalRtt += ack.RTT;
            }
        }

        var lossRate = 1 - (received / (float)s_Metrics[id].Count);
        var avgRtt = received > 0 ? totalRtt / received : 0;

        Console.WriteLine($"[Pop {Utils.Int32ToIp(id)}] Metrics - Packet Loss: {lossRate:P}, Avg RTT: {avgRtt}ms");

        s_MetricsData[id] = new Metrics(null, avgRtt, lossRate);
    }

    private void StartUdpStream() {
        NetworkMessenger.StartUdpClient(Consts.UdpPort, async client => {
            try {
                while (m_IsRunning) {
                    var receivedPackets = new Dictionary<string, List<byte[]>>();
                    var result = await client.ReceiveAsync();
                    PacketReader reader = new(result.Buffer);

                    if (reader.GetOpCode(out _).OpCode != OpCodes.VideoStream) continue;

                    reader.GetArguments(out var args);

                    if (args.Length < 5) {
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
            catch (Exception ex) {
                Console.WriteLine($"Error receiving UDP stream: {ex.Message}\n{ex.StackTrace}");
            }
            finally {
                Close();
            }
        });
    }

    private void DisplayFrame(byte[] frameBuffer) {
        using var stream = new MemoryStream(frameBuffer);
        var bitmap = new Bitmap(stream);
        StreamImage.Source = bitmap;
    }

    public void Close() {
        m_IsRunning = false;
        NetworkMessenger.DisposeUdpClient(Consts.UdpPort);
    }
    
    public void UpdatePops(List<string[]> Pops)
    {
        isUpdatingComboBox = true;
        s_Pops.Clear();
        s_Pops.Add(new string[] { "None", "" });
        s_Pops.AddRange(Pops);
        PopsComboBox.SelectedIndex = 0;
        isUpdatingComboBox = false;
    }
    
    public void UpdateContentIds(List<string> ContentIds)
    {
        isUpdatingComboBox = true;
        s_ContentIds.Clear();
        s_ContentIds.Add("None");
        s_ContentIds.AddRange(ContentIds);
        ContentIdComboBox.SelectedIndex = 0;
        isUpdatingComboBox = false;
    }

// Declare variables to store the previous POP and Content ID
private string previousPop = "None";  // Default value before any POP is selected
private string previousContentId = "None";  // Default value before any Content ID is selected

private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) {
    // Skip processing if the ComboBox is being updated
    if (isUpdatingComboBox) return;

    // Check if either the POP or Content ID has changed
    var selectedPop = (string[])PopsComboBox.SelectedItem;
    var selectedContentId = ContentIdComboBox.SelectedItem?.ToString();

    // Log the current selections
    Console.WriteLine($"[OnSelectionChanged] Previous POP: {previousPop}, Previous Content ID: {previousContentId}");
    Console.WriteLine($"[OnSelectionChanged] Selected POP: {selectedPop[0]}, Selected Content ID: {selectedContentId}");

    // If the previous POP is different from the selected POP, stop streaming from the previous POP
    if (previousPop != "None" && previousPop != selectedPop[0]) {
        Console.WriteLine($"[OnSelectionChanged] Stopping streaming from previous POP: {previousPop}");
        NetworkMessenger.Send(previousPop, Consts.TcpPortPopListener, OpCodes.StopStreaming, false);
    }

    // If the previous Content ID is different from the selected Content ID, stop streaming from the previous content
    if (previousContentId != "None" && previousContentId != selectedContentId) {
        Console.WriteLine($"[OnSelectionChanged] Stopping streaming from previous Content ID: {previousContentId}");
        NetworkMessenger.Send(selectedPop[0], Consts.TcpPortPopListener, OpCodes.StopStreaming, false);
    }

    // If the selected index is 0 for either ComboBox, return early
    if (PopsComboBox.SelectedIndex == 0 || ContentIdComboBox.SelectedIndex == 0) {
        previousPop = "None";
        previousContentId = "None";
        return;
    }

    // Update the previous values with the current selections
    previousPop = selectedPop[0];
    previousContentId = selectedContentId;

    // Start streaming from the new POP and Content ID
    Console.WriteLine("[OnSelectionChanged] Starting streaming from the new POP and Content ID.");
    NetworkMessenger.Send(selectedPop[0], Consts.TcpPortPopListener, OpCodes.StartStreaming, false, selectedContentId);
}

}