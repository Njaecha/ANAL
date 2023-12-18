using System;
using System.Collections.Generic;
using System.Text;
using MessagePack;
using UnityEngine;
using LogicFlows;

namespace AmazingNewAccessoryLogic
{
    [MessagePackObject]
    public class SerialisedNode
    {
        [Key("Type")]
        public NodeType type { get; set; }

        [Key("Position")]
        public Vector2 postion { get; set; }

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

        public enum NodeType
        {
            Gate_NOT,
            Gate_AND,
            Gate_OR,
            Gate_XOR,
            Input,
            Output,
            Custom
        }
        
        public static SerialisedNode Serialise(LogicFlowNode node)
        {
            if (node is LogicFlowGate g) return fromGate(g);
            else if (node is LogicFlowOutput n) return fromOutput(n);
            else if (node is LogicFlowInput i) return fromInput(i);
            else return null;
        }

        public static SerialisedNode fromInput(LogicFlowInput input)
        {
            SerialisedNode sn = new SerialisedNode();
            sn.type = NodeType.Input;
            sn.postion = input.getPosition();
            sn.index = input.index;
            sn.enabled = input.enabled;
            sn.data = new List<int>() { input.index };
            return sn;
        }
        public static SerialisedNode fromOutput(LogicFlowOutput output)
        {
            SerialisedNode sn = new SerialisedNode();
            sn.type = NodeType.Output;
            sn.postion = output.getPosition();
            sn.index = output.index;
            sn.enabled = output.enabled;
            sn.data = new List<int>() { 
                output.index - 1000000, 
                output.inputAt(0).index 
            };
            return sn;
        }

        public static SerialisedNode fromGate(LogicFlowGate gate)
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
            sn.postion = gate.getPosition();
            sn.index = gate.index;
            sn.enabled = gate.enabled;
            List<int> inputs = new List<int>();
            for(int i = 0; i < gate.inputAmount; i++)
            {
                inputs.Add(gate.inputAt(i).index);
            }
            sn.data = inputs;
            return sn;
        }
    }
}
