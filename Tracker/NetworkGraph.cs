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
                Connections = []
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
                    Weight = 1
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
        var shortestPath = new List<Node>();

        Node? startNode = null;
        Node? endNode = null;

        foreach (var node in Nodes)
        {
            unvisited.Add(node);

            if (node.HasAlias(end)) endNode = node;
            if (node.HasAlias(start))
            {
                distances[node] = int.MaxValue;
                continue;
            }

            startNode = node;
            distances[node] = 0;
        }

        if (unvisited.Count == 0 || startNode == null || endNode == null) return [];

        var current = startNode;
        unvisited.Remove(current);

        while (unvisited.Count > 0)
        {
            foreach (var connection in current.Connections)
            {
                if (!TryGetNode(connection.Id, out var otherNode)) continue;
                if (!unvisited.Contains(otherNode)) continue;

                var newDistance = distances[current] + connection.Weight;
                if (newDistance < distances[otherNode])
                {
                    distances[otherNode] = newDistance;
                }
            }

            var smallest = int.MaxValue;
            Node? next = null;
            foreach (var node in unvisited)
            {
                if (distances[node] >= smallest) continue;
                smallest = distances[node];
                next = node;
            }

            if (next == null) break;
            current = next;
            unvisited.Remove(current);
        }

        current = endNode;
        shortestPath.Add(current);

        Node? lastNode = null;
        while (!current.HasAlias(start))
        {
            if (lastNode != null && lastNode.Alias == current.Alias)
            {
                throw new Exception("Couldn't find a path back");
            }

            lastNode = current;

            foreach (var connection in current.Connections)
            {
                if (!TryGetNode(connection.Id, out var otherNode)) continue;
                if (distances[otherNode] != distances[current] - connection.Weight) continue;
                shortestPath.Add(otherNode);
                current = otherNode;
                break;
            }
        }

        var copy = new List<Node>(shortestPath);
        for (var i = 0; i < shortestPath.Count; i++)
        {
            shortestPath[i] = copy[^(i + 1)];
        }

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