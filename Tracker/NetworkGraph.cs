using ESR.Shared;

namespace ESR.Tracker;

public class NetworkGraph
{
    public struct Connection
    {
        public int Id { get; init; }
        public int Weight { get; set; }
    }

    public class Node
    {
        private static int s_IdCounter;
        public int Id { get; } = s_IdCounter++;
        public bool IsConnected = false;
        public bool IsPOP = false;
        public int[] Alias { get; init; } = [];
        public List<Connection> Connections { get; init; } = [];

        public bool HasAlias(int alias)
        {
            for (var i = 0; i < Alias.Length; i++)
            {
                if (Alias[i] == alias) return true;
            }

            return false;
        }
    }

    public NetworkGraph(NodeNet nodeNet)
    {
        for (var i = 0; i < nodeNet.Nodes.Length; i++)
        {
            var node = new Node
            {
                Alias = Utils.IpAliasToInt32(nodeNet.Nodes[i].IpAddressAlias),
                Connections = [],
                IsPOP =  nodeNet.Nodes[i].IsPOP
            };

            Nodes.Add(node);
        }

        for (var i = 0; i < nodeNet.Nodes.Length; i++)
        {
            var node = Nodes[i];
            var connections = nodeNet.Nodes[i].Connections;
            
            for (var j = 0; j < connections.Length; j++)
            {
                var connection = connections[j];
                var otherNode = Nodes.Find(n => n.HasAlias(Utils.IpToInt32(connection)));
                if (otherNode == null) continue;
                node.Connections.Add(new Connection
                {
                    Id = otherNode.Id,
                    Weight = 1//((node.Alias[0] == Utils.IpToInt32("10.0.8.2")) && (otherNode.Alias[0] == Utils.IpToInt32("10.0.1.2"))) ? 500 : 1
                });
            }
        }
    }

    public List<Node> GetShortestPath(string startIP, string endIP)
    {
        var start = Utils.IpToInt32(startIP);
        var end = Utils.IpToInt32(endIP);

        var unvisited = new HashSet<Node>();
        var distances = new Dictionary<Node, int>();
        var previousNodes = new Dictionary<Node, Node?>(); 
        var shortestPath = new List<Node>();

        Node? startNode = null;
        Node? endNode = null;

        foreach (var node in Nodes)
        { 
            if (!node.IsConnected) continue;
            unvisited.Add(node);

            if (node.HasAlias(end)) endNode = node;
            if (node.HasAlias(start)) startNode = node;

            distances[node] = int.MaxValue;
            previousNodes[node] = null; 
        }

        if (unvisited.Count == 0 || startNode == null || endNode == null) return shortestPath;

        distances[startNode] = 0;

        while (unvisited.Count > 0)
        {
            var current = unvisited.OrderBy(node => distances[node]).FirstOrDefault();
            if (current == null || distances[current] == int.MaxValue) break;

            unvisited.Remove(current);

            foreach (var connection in current.Connections)
            {
                if (!TryGetNode(connection.Id, out var neighbor)) continue;
                if (!unvisited.Contains(neighbor)) continue;

                var newDistance = distances[current] + connection.Weight;
                if (newDistance < distances[neighbor])
                {
                    distances[neighbor] = newDistance;
                    previousNodes[neighbor] = current;
                }
            }
        }
        
        var currentPathNode = endNode;
        while (currentPathNode != null)
        {
            shortestPath.Add(currentPathNode);
            currentPathNode = previousNodes[currentPathNode];
        }

        shortestPath.Reverse();
        return shortestPath;
    }
    
    private bool TryGetNode(int id, out Node result)
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (node.Id != id) continue;
            result = node;
            return true;
        }

        result = null!;
        return false;
    }
    
    public Node? GetAliasNode(int ip)
    {
        return Nodes.Find(n => n.HasAlias(ip));
    }
    
    public Node? GetNode(int id)
    {
        return Nodes.Find(n => n.Id == id);
    }

    public void UpdateNode(int ip, bool isConnected)
    {
        var node = GetAliasNode(ip);
        if (node == null) return;
        node.IsConnected = isConnected;
    }
    
    public List<Node> Nodes { get; set; } = [];
}