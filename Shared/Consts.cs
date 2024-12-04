namespace ESR.Shared;

public static class Consts
{
    public const string TrackerIpAddress = "10.0.14.10";
    public const string StreamerIpAddress = "10.0.13.10";
    
    public const int TcpPort = 27015; // Default port for TCP connections
    public const int TcpPortPopListener = 27016; // Default port for TCP POP listener
    public const int UdpPort = 27020; // Default port for UDP connections
    public const int UdpPortMetricsSender = 27021; // Default port for UDP metrics
    public const int UdpPortMetricsListener = 27022; // Default port for UDP metrics response
    public const int UdpPortMetricsAckListener = 27023; // Default port for UDP metrics ack
    
    public const int Timeout = 1000; // Timeout for the connection
    public const int ErrorTimeout = 3000; // Sleep for Xms before retrying
    public const string MjpegFilePath = "stream.jpeg";
    public const string StreamingDirectory = "Content";
    
    public const int FrameRate = 30; // Frames per second for the video
}