namespace ESR.Shared
{
    public enum OpCodes
    {
        Disconnect = -1,
        None = 0x00,
        StartStreaming = 0x01,
        StopStreaming = 0x02,
        Bootstrap = 0x03,
        Metrics = 0x04,
        MetricsAck = 0x05,
        NodeUpdate = 0x06,
        Shutdown = 0xFF
    }
}
