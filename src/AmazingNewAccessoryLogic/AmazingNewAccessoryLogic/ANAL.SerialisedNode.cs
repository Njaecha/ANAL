using LogicFlows;
using MessagePack;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace AmazingNewAccessoryLogic
{
    [MessagePackObject]
    public class SerialisedNode
    {
        [Key("Type")]
        public NodeType type { get; set; }

        [Key("Position")]
        public Vector2 position { get; set; }

        [Key("Index")]
        public int index { get; set; }

        [Key("Enabled")]
        public bool enabled { get; set; }

        /// <summary>
        /// Gates: Input-NodeIndex...
        /// Input: Identifier
        /// Output: AccessorySlot, Input-NoteIndex...
        /// </summary>
        [Key("Data")]
        public List<int> data { get; set; }

        [Key("Advanced Data")]
        public List<object> data2 { get; set; } = null;

        [Key("Group Data")]
        public Dictionary<int, List<int>> data3 { get; set; } = null;

        [Key("Name")]
        public string name { get; set; } = null;

        public enum NodeType
        {
            Gate_NOT,
            Gate_AND,
            Gate_OR,
            Gate_XOR,
            Input,
            Output,
            AdvancedInput,
            Gate_GRP,
        }
        
        public static SerialisedNode Serialise(LogicFlowNode node, LogicFlowGraph graph)
        {
            if (node is LogicFlowNode_GRP grp) return fromGroup(grp, graph);
            else if (node is LogicFlowGate g) return fromGate(g, graph);
            else if (node is LogicFlowOutput n) return fromOutput(n, graph);
            else if (node is LogicFlowInput i) return fromInput(i, graph);
            else return null;
        }

        public static SerialisedNode fromInput(LogicFlowInput input, LogicFlowGraph graph)
        {
            SerialisedNode sn = new SerialisedNode();
            if (AnalCharaController.serialisationData.TryGetValue(graph, out var dicGraph) && dicGraph.TryGetValue(input.index, out var sData)) {
                sn.data2 = sData;
                sn.type = NodeType.AdvancedInput;
            } else {
                sn.type = NodeType.Input;
            }
            sn.position = input.getPosition();
            sn.index = input.index;
            sn.enabled = input.enabled;
            sn.data = new List<int>();
            sn.name = input.label;
            return sn;
        }

        public static SerialisedNode fromOutput(LogicFlowOutput output, LogicFlowGraph graph)
        {
            SerialisedNode sn = new SerialisedNode();
            sn.type = NodeType.Output;
            sn.position = output.getPosition();
            sn.index = output.index;
            sn.enabled = output.enabled;
            sn.data = new List<int>();
            if (output.inputAt(0) != null) {
                sn.data.Add(output.inputAt(0).index); 
            }
            sn.name = output.label;
            return sn;
        }

        public static SerialisedNode fromGate(LogicFlowGate gate, LogicFlowGraph graph)
        {
            SerialisedNode sn = new SerialisedNode();
            switch (gate)
            {
                case LogicFlowNode_NOT _:
                    sn.type = NodeType.Gate_NOT;
                    break;
                case LogicFlowNode_AND _:
                    sn.type = NodeType.Gate_AND;
                    break;
                case LogicFlowNode_OR _:
                    sn.type = NodeType.Gate_OR;
                    break;
                case LogicFlowNode_XOR _:
                    sn.type = NodeType.Gate_XOR;
                    break;
            }
            sn.position = gate.getPosition();
            sn.index = gate.index;
            sn.enabled = gate.enabled;
            List<int> inputs = new List<int>();
            for(int i = 0; i < gate.inputAmount; i++) {
                if (gate.inputAt(i) != null) inputs.Add(gate.inputAt(i).index);
            }
            sn.data = inputs;
            sn.name = gate.label;
            return sn;
        }

        public static SerialisedNode fromGroup(LogicFlowNode_GRP grp, LogicFlowGraph graph) {
            SerialisedNode sn = new SerialisedNode() {
                type = NodeType.Gate_GRP,
                position = grp.getPosition(),
                index = grp.index,
                enabled = grp.enabled
            };
            if (grp.inputs[0] != null) {
                sn.data = new List<int> { grp.inputs[0].Value };
            } else {
                sn.data = new List<int>();
            }
            sn.data2 = new List<object> { grp.state };
            sn.data3 = new Dictionary<int, List<int>>();
            foreach (var kvp in grp.controlledNodes
                .Where(x => x.Value.Count > 0)
                .Select(x =>
                    new KeyValuePair<int, List<int>>(
                        x.Key,
                        x.Value
                            .Where(y =>
                                y.inputs.Any(z =>
                                    z != null &&
                                    graph.getNodeAt(z.Value) == grp
                                )
                            )
                            .Select(y => y.index)
                            .ToList()
                    )
                )
            ) {
                sn.data3[kvp.Key] = kvp.Value;
            }
            sn.name = grp.getName();
            return sn;
        }
    }
}
