using ESR.Shared;
using static ESR.Tracker.NetworkGraph;

namespace ESR.Tracker;

public class SBT {
    public Node Source { get; set; }
    public Dictionary<Node, List<Node>> AdjacencyList { get; set; } = new();

    public void AddEdge(Node parent, Node child)
    {
        if (!AdjacencyList.ContainsKey(parent))
        {
            AdjacencyList[parent] = new List<Node>();
        }
        AdjacencyList[parent].Add(child);
    }

    public List<Node> GetChildren(Node node)
    {
        return AdjacencyList.ContainsKey(node) ? AdjacencyList[node] : new List<Node>();
    }
    
    public static SBT BuildSBT(Node source, NetworkGraph graph)
    {
        var sbt = new SBT { Source = source };
        var visitedNodes = new HashSet<(Node Parent, Node Child)>();
        foreach (var node in graph.Nodes)
        {
            if (!node.IsPOP) continue; 
            var path = graph.GetShortestPath(Utils.Int32ToIp(source.Alias[0]), Utils.Int32ToIp(node.Alias[0]));

            for (int i = 0; i < path.Count - 1; i++)
            {
                Node parent = path[i];
                Node child = path[i + 1];
                
                if (!visitedNodes.Contains((parent, child)))
                {
                    sbt.AddEdge(parent, child);
                    visitedNodes.Add((parent, child));
                }
            }
        }

        return sbt;
    }
}