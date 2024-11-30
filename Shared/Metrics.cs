namespace ESR.Shared;

public class Metrics {
    public float AverageRTT { get; }
    public float PacketLoss { get; }

    public Metrics(float averageRTT, float packetLoss) {
        AverageRTT = averageRTT;
        PacketLoss = packetLoss;
    }
}