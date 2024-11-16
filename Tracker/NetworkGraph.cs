using ESR.Shared;

namespace ESR.Tracker;

public class NetworkGraph
{
    public struct Connection
    {
        public int Ip { get; init; }
        public int Weight { get; set; }
    }

    public class Node
    {
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

            foreach (var connection in nodeNet.Nodes[i].Connections)
            {
                var existingNode = Nodes.Find(n => n.HasAlias(Utils.IpToInt32(connection)));
                // TODO - Implement Weighted Connections
                var weight = existingNode == null ? Random.Shared.Next(1, 25) : existingNode.Connections.Find(c => node.HasAlias(c.Ip)).Weight;

                node.Connections.Add(new Connection
                {
                    Ip = Utils.IpToInt32(connection),
                    Weight = weight
                });
            }

            Nodes.Add(node);
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
                if (!TryGetNode(connection.Ip, out var otherNode)) continue;
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
                if (!TryGetNode(connection.Ip, out var otherNode)) continue;
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

    public void UpdateWeights(string parent, string child, int weight)
    {
        if (!TryGetNode(Utils.IpToInt32(parent), out var parentNode)) return;
        for (var i = 0; i < parentNode.Connections.Count; i++)
        {
            var con = parentNode.Connections[i];
            if (con.Ip != Utils.IpToInt32(child)) continue;
            con.Weight = weight;
            parentNode.Connections[i] = con;
        }
    }

    private bool TryGetNode(int id, out Node result)
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (node.HasAlias(id)) continue;
            result = node;
            return true;
        }

        result = null!;
        return false;
    }

    public List<Node> Nodes { get; set; } = [];
}