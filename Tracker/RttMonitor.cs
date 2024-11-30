using ESR.Shared;
using System.Collections.Concurrent;

namespace ESR.Tracker;

public class RttMonitor
{
    private double alpha;         // Smoothing factor for moving average (Lower = Smoother)
    private double k;             // Multiplier for the thresholds
    private double rttAverage;    // Moving average of RTT
    private double rttStdDev;     // Moving standard deviation of RTT
    private double packetLossAverage; // Moving average of packet loss
    private double packetLossStdDev;  // Moving standard deviation of packet loss

    public double RttAverage => rttAverage;
    public double PacketLossAverage => packetLossAverage;

    public event Action NetworkStateChanged;

    public RttMonitor(double Alpha = 0.2, double K = 2.0)
    {
        alpha = Alpha;
        k = K;
        rttAverage = 0.0;
        rttStdDev = 0.0;
        packetLossAverage = 0.0;
        packetLossStdDev = 0.0;
    }
    public bool UpdateRtt(double newRtt)
    {
        rttAverage = alpha * newRtt + (1 - alpha) * rttAverage;

        rttStdDev = Math.Sqrt(alpha * Math.Pow(newRtt - rttAverage, 2) + (1 - alpha) * Math.Pow(rttStdDev, 2));

        double upperThreshold = rttAverage + k * rttStdDev;
        double lowerThreshold = rttAverage - k * rttStdDev;

        bool stateChanged = newRtt > upperThreshold || newRtt < lowerThreshold;

        if (stateChanged) NetworkStateChanged?.Invoke();

        Console.WriteLine($"New RTT: {newRtt:F2}, Average: {rttAverage:F2}, StdDev: {rttStdDev:F2}, Thresholds: [{lowerThreshold:F2}, {upperThreshold:F2}]");
        Console.WriteLine(stateChanged ? "Network state change detected!" : "Network state is stable.");

        return stateChanged;
    }

    public bool UpdatePacketLoss(double newPacketLoss)
    {
        packetLossAverage = alpha * newPacketLoss + (1 - alpha) * packetLossAverage;

        packetLossStdDev = Math.Sqrt(alpha * Math.Pow(newPacketLoss - packetLossAverage, 2) + (1 - alpha) * Math.Pow(packetLossStdDev, 2));

        double upperThreshold = packetLossAverage + k * packetLossStdDev;
        double lowerThreshold = packetLossAverage - k * packetLossStdDev;

        bool stateChanged = newPacketLoss > upperThreshold || newPacketLoss < lowerThreshold;

        if (stateChanged) NetworkStateChanged?.Invoke();

        Console.WriteLine($"New Packet Loss: {newPacketLoss:F2}%, Average: {packetLossAverage:F2}%, StdDev: {packetLossStdDev:F2}, Thresholds: [{lowerThreshold:F2}%, {upperThreshold:F2}%]");
        Console.WriteLine(stateChanged ? "Network state change detected (Packet Loss)!" : "Network state is stable (Packet Loss).");

        return stateChanged;
    }
    
}