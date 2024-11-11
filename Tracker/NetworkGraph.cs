using ESR.Shared;

namespace ESR.Tracker;

public class NetworkGraph
    {
        public struct Connection
        {
            public int OtherNode { get; init; }
            public int Weight { get; set; }
        }

        public class Node
        {
            public int Id { get; init; }
            public List<Connection> Connections { get; init; } = [];
        }

        public NetworkGraph(NodeNet nodeNet)
        {
            for (var i = 0; i < nodeNet.Nodes.Length; i++)
            {
                var node = new Node
                {
                    Id = Utils.IpToInt32(nodeNet.Nodes[i].IpAddress),
                    Connections = []
                };

                foreach (var connection in nodeNet.Nodes[i].Connections)
                {
                    Node? existingNode = Nodes.Find(n => n.Id == Utils.IpToInt32(connection));
                    // TODO - Implement Weighted Connections
                    var weight = existingNode == null ? Random.Shared.Next(1, 25) : existingNode.Connections.Find(c => c.OtherNode == node.Id).Weight;
                    
                    node.Connections.Add(new Connection
                    {
                        OtherNode = Utils.IpToInt32(connection),
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
                
                if (node.Id == end) endNode = node;
                
                if (node.Id != start)
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
            
            Console.WriteLine("Starting Dijkstra's Algorithm");
            while (unvisited.Count > 0)
            {
                foreach (var connection in current.Connections)
                {
                    if (!TryGetNode(connection.OtherNode, out var otherNode)) continue;
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

            // TODO - Handle cases when there is no back path
            Console.WriteLine("Finding Shortest Path from End to Start");
            Node? lastNode = null;
            while (current.Id != start)
            {
                if (lastNode != null && lastNode.Id == current.Id)
                {
                    throw new Exception("Couldn't find a path back");
                }
                lastNode = current;
                
                foreach (var connection in current.Connections)
                {
                    if (!TryGetNode(connection.OtherNode, out var otherNode)) continue;
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
            foreach (var node in Nodes)
            {
                if (node.Id != id) continue;
                result = node;
                return true;
            }

            result = null!;
            return false;
        }

        public List<Node> Nodes { get; set; } = [];
    }