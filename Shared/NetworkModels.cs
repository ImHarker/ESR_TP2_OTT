﻿using System.Text.Json.Serialization;

namespace ESR.Shared;

public class NodeConnection()
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
    [JsonPropertyName("alias")]
    public string[] Aliases { get; init; }
    [JsonPropertyName("connected")]
    public bool Connected { get; set; } = false;
}

public struct NodeResponse
{
    public bool IsPop { get; init; }
    [JsonPropertyName("connections")]
    public List<NodeConnection> Connections { get; init; }
}

public struct ContentMetadata {
    [JsonPropertyName("contentId")] 
    public List<string> ContentIds { get; init; }
    [JsonPropertyName("pops")] 
    public List<string[]> Pops { get; init; }
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