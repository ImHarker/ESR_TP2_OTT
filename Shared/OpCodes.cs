namespace ESR.Shared
{
    public enum OpCodes
    {
        None = 0x00,
        StartStreaming = 0x01,
        StopStreaming = 0x02,
        GetNodes = 0x03,
        Shutdown = 0xFF
    }
}
