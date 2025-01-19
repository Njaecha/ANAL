using System;
using System.Collections.Generic;
using System.Text;
using MessagePack;
using UnityEngine;
using LogicFlows;

namespace AmazingNewAccessoryLogic
{
    [MessagePackObject]
    public class SerialisedGraph
    {
        [Key("Advanced")]
        public bool advanced { get; set; }
        [Key("Size")]
        public Vector2 size { get; set; }
        [Key("Nodes")]
        public List<SerialisedNode> nodes { get; set; }

        public static SerialisedGraph Serialise(LogicFlowGraph graph, bool advanced = false)
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
            sg.advanced = advanced;
            sg.nodes = nodeList;
            return sg;
        }
    }
}
