namespace ESR.Shared;

public static class Consts
{
    public const string TrackerIpAddress = "127.0.0.1";
    
    public const int TcpPort = 27015; // Default port for TCP connections
    public const int UdpPort = 27020; // Default port for UDP connections
    public const int UdpPortHeartbeat = 27021; // Default port for UDP heartbeat
    public const int UdpPortHeartbeatResponse = 27022; // Default port for UDP heartbeat response
    
    public const int Timeout = 1000; // Timeout for the connection
    public const int ErrorTimeout = 3000; // Sleep for Xms before retrying
    public const string MjpegFilePath = "stream.jpeg";
    
    public const int FrameRate = 30; // Frames per second for the video
}