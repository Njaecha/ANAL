using LogicFlows;
using MessagePack;
using UnityEngine;
using System.Collections.Generic;

namespace AmazingNewAccessoryLogic
{
    [MessagePackObject]
    public class SerialisedGraph
    {
        [Key("Size")] public Vector2 size { get; set; }
        [Key("Nodes")] public List<SerialisedNode> nodes { get; set; }

        public static SerialisedGraph Serialise(LogicFlowGraph graph)
        {
            SerialisedGraph sg = new SerialisedGraph();
            sg.size = graph.getSize();
            List<SerialisedNode> nodeList = new List<SerialisedNode>();
            foreach (LogicFlowNode n in graph.getAllNodes())
            {
                SerialisedNode sNode = SerialisedNode.Serialise(n, graph);
                if (sNode != null)
                {
                    nodeList.Add(sNode);
                }
            }

            sg.nodes = nodeList;
            return sg;
        }
    }
}