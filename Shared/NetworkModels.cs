using System.Text.Json.Serialization;

namespace ESR.Shared;

public struct NodeResponse
{
    [JsonPropertyName("connections")]
    public string[] Connections { get; init; }
    [JsonPropertyName("isPOP")]
    public bool IsPOP { get; init; }
}

public struct NodeNet
{
    public struct Node
    {
        [JsonPropertyName("ip")]
        public string[] IpAddressAlias { get; init; }
        [JsonPropertyName("connections")]
        public string[] Connections { get; init; }
        [JsonPropertyName("isPOP")]
        public bool IsPOP { get; init; }
    }
    
    [JsonPropertyName("nodes")]
    public Node[] Nodes { get; init; }
}