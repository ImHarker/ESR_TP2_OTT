namespace ESR.Shared;

public class Metrics {
    public NodeConnection Connection { get; }
    public float AverageRTT { get; }
    public float PacketLoss { get; }

    public Metrics(NodeConnection connection, float averageRTT, float packetLoss) {
        Connection = connection;
        AverageRTT = averageRTT;
        PacketLoss = packetLoss;
    }
}