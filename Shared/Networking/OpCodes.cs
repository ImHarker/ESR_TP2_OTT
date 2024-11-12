namespace ESR.Shared
{
    public enum OpCodes
    {
        None = 0x00,
        Heartbeat = 0x01,
        StartStreaming = 0x02,
        StopStreaming = 0x03,
        GetNodes = 0x04,
        Shutdown = 0xFF
    }
}
