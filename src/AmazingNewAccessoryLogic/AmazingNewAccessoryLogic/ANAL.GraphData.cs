using ActionGame.Point;
using LogicFlows;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AmazingNewAccessoryLogic {
    public class GraphData {
        public LogicFlowGraph graph;
        private bool _advanced;
        public bool advanced {
            get {
                return _advanced;
            }
            set {
                _advanced = value;
            }
        }

        private static Dictionary<BindingType, List<InputKey>> _bindingStates = null;
        public static Dictionary<BindingType, List<InputKey>> bindingStates {
            get {
                if (_bindingStates == null) {
                    _bindingStates = new Dictionary<BindingType, List<InputKey>> {
                        { BindingType.Top, new List<InputKey> { InputKey.TopOn, InputKey.TopShift } },
                        { BindingType.Bottom, new List<InputKey> { InputKey.BottomOn, InputKey.BottomShift } },
                        { BindingType.Bra, new List<InputKey> { InputKey.BraOn, InputKey.BraShift } },
                        { BindingType.Underwear, new List<InputKey> { InputKey.UnderwearOn, InputKey.UnderwearShift, InputKey.UnderwearHang } },
                        { BindingType.Gloves, new List<InputKey> { InputKey.GlovesOn, InputKey.GlovesShift, InputKey.GlovesHang } },
                        { BindingType.Pantyhose, new List<InputKey> { InputKey.PantyhoseOn, InputKey.PantyhoseShift, InputKey.PantyhoseHang } },
                        { BindingType.Legwear, new List<InputKey> { InputKey.LegwearOn } },
                        { BindingType.Shoes, new List<InputKey> {
#if KKS
                            InputKey.ShoesOn
#else
                            InputKey.ShoesIndoorOn,
                            InputKey.ShoesOutdoorOn,
#endif
                        } },
                    };
                }
                return _bindingStates;
            }
        }

        private Dictionary<int, List<int>> groupChildren = new Dictionary<int, List<int>>();
        public Dictionary<int, BindingType?> bindings = new Dictionary<int, BindingType?>();
        public Dictionary<int, byte> activeBoundStates = new Dictionary<int, byte>();

        public GraphData(LogicFlowGraph logicGraph, bool isAdvanced) {
            graph = logicGraph;
            _advanced = isAdvanced;
        }

        public void AddChild(int grpIdx, int childSlot) {
            if (!groupChildren.ContainsKey(grpIdx)) {
                groupChildren.Add(grpIdx, new List<int>());
            }
            groupChildren[grpIdx].Add(childSlot);

            var ctrl = AnalCharaController.dicGraphToControl[graph];
            int outfit = ctrl.graphs.Keys.FirstOrDefault(x => ctrl.graphs[x] == graph);
            if (ctrl.getOutput(childSlot, outfit) == null) {
                ctrl.addOutput(childSlot, outfit);
            }

            MakeGraph();
        }

        public bool RemoveChild(int grpIdx, int childSlot) {
            if (!groupChildren.ContainsKey(grpIdx)) return false;

            bool result = groupChildren[grpIdx].Remove(childSlot);
            MakeGraph();
            return result;
        }

        public bool TryGetChildren(int grpIdx, out List<int> children) {
            return groupChildren.TryGetValue(grpIdx, out children);
        }

        public HashSet<int> GetAllChildIndices() {
            var result = new HashSet<int>();
            foreach (var kvp in groupChildren) {
                foreach (var child in kvp.Value) {
                    result.Add(child);
                }
            }
            return result;
        }

        public void RemoveGroup(int grpIdx) {
            groupChildren.Remove(grpIdx);
            MakeGraph();
        }

        public void MakeGraph() {
            // Only make the graph in simple mode
            if (advanced) return;

            // Get control and current outfit
            var ctrl = AnalCharaController.dicGraphToControl[graph];
            int outfit = ctrl.graphs.Keys.FirstOrDefault(x => ctrl.graphs[x] == graph);

            // Clear graph of everything we don't want to reuse
            List<int> toRemove = new List<int>();
            foreach (var node in graph.nodes) {
                switch (node.Value) {
                    case LogicFlowNode_AND and:
                    case LogicFlowNode_NOT not:
                    case LogicFlowNode_OR or:
                    case LogicFlowNode_XOR xor:
                        toRemove.Add(node.Key);
                        break;
                    case LogicFlowNode_GRP grp:
                    case LogicFlowOutput output:
                        node.Value.inputs[0] = null;
                        break;
                }
            }
            foreach (int idx in toRemove) graph.RemoveNode(idx);

            // Connect outputs to group nodes
            foreach (var kvp in groupChildren) {
                var grp = (LogicFlowNode_GRP)graph.getNodeAt(kvp.Key);
                Vector2 pos = Vector2.zero;
                foreach (var slot in kvp.Value) {
                    var node = ctrl.getOutput(slot, outfit);
                    node.SetInput(kvp.Key, 0);
                    pos += node.getPosition();
                }
                if (kvp.Value.Count > 0) {
                    pos /= kvp.Value.Count;
                    grp.setPosition(pos - new Vector2(grp.getSize().x + 30, 0));
                } else {
                    grp.setPosition(graph.getSize() / 2 - (grp.getSize() / 2));
                }
            }

            // Connect clothing states to requisite nodes
            var allChildren = GetAllChildIndices();
            foreach (var kvp in bindings) {
                // Skip unbound and in-group accessories
                if (kvp.Value == null) continue;
                if (allChildren.Contains(kvp.Key)) continue;

                // Get or make output
                LogicFlowNode boundNode;
                if (kvp.Key < 0) {
                    boundNode = graph.getNodeAt(kvp.Key);
                } else {
                    boundNode = ctrl.getOutput(kvp.Key, outfit);
                    if (boundNode == null) {
                        boundNode = ctrl.addOutput(kvp.Key, outfit);
                    }
                }
                if (boundNode == null) continue;

                // Decode binding keys
                var bindKeys = bindingStates[kvp.Value.Value];
                List<int> activeBindings = new List<int>();
                for (int i = 0; i < 4; i++) {
                    if ((activeBoundStates[kvp.Key] & (1 << i)) > 0) {
                        if (i < 3) {
                            if (bindKeys.Count > i) activeBindings.Add((int)bindKeys[i]);
                        } else {
                            activeBindings.Add(0);
                        }
                    }
                }

                // Connect inputs to node
                float perGateOffset = 20f;
                if (activeBindings.Count == 0) {
                    // No bound states, node should always be inactive
                    var not = ctrl.addNotForInput((int)bindKeys[0], outfit);
                    var and = ctrl.addAndGateForInputs((int)bindKeys[0], not.index, outfit);
                    and.setPosition(boundNode.getPosition() - new Vector2(and.getSize().x + perGateOffset, 0));
                    not.setPosition(and.getPosition() - new Vector2(not.getSize().x + perGateOffset, 0));
                    boundNode.SetInput(and.index, 0);
                } else if (activeBindings.Count == 1) {
                    // Only one state is bound
                    if (activeBindings[0] == 0) {
                        // But that state is the Off state, so we have to invert all the rest
                        var none = makeNone(out var noneNodes);
                        noneNodes.Reverse();
                        LogicFlowNode previous = boundNode;
                        foreach (var node in noneNodes) {
                            node.setPosition(previous.getPosition() - new Vector2(node.getSize().x + perGateOffset, 0));
                            previous = node;
                        }
                        boundNode.SetInput(none.index, 0);
                    } else {
                        // We can bind the single, non-off state directly to the node
                        boundNode.SetInput(activeBindings[0], 0);
                    }
                } else if (activeBindings.Count == bindKeys.Count + 1) {
                    // All possible states are bound, node should always be active -> no binding needed
                    continue;
                } else if (activeBindings.Count == bindKeys.Count) {
                    // All but one state is bound
                    if (activeBindings.Contains(0)) {
                        // The off input is bound, so we can invert the single remaining input
                        int inverse = (int)bindKeys.Where(x => !activeBindings.Contains((int)x)).First();
                        var not = ctrl.addNotForInput(inverse, outfit);
                        not.setPosition(boundNode.getPosition() - new Vector2(not.getSize().x + perGateOffset, 0));
                    } else {
                        // The off input isn't bound so we have to OR together either two or three inputs
                        // Because the one input scenario was handled earlier
                        var or1 = ctrl.addOrGateForInputs(activeBindings[1], activeBindings[0], outfit);
                        if (activeBindings.Count > 2) {
                            var or2 = ctrl.addOrGateForInputs(or1.index, activeBindings[2], outfit);
                            or2.setPosition(boundNode.getPosition() - new Vector2(or2.getSize().x + perGateOffset, 0));
                            or1.setPosition(or2.getPosition() - new Vector2(or1.getSize().x + perGateOffset, 0));
                            boundNode.SetInput(or2.index, 0);
                        } else {
                            or1.setPosition(boundNode.getPosition() - new Vector2(or1.getSize().x + perGateOffset, 0));
                            boundNode.SetInput(or1.index, 0);
                        }
                    }
                } else {
                    // The only remaining scenario is where two out of four possible states are bound
                    if (activeBindings.Contains(0)) {
                        // The off state is bound, so we invert the OR of the other two
                        List<InputKey> inverse = bindKeys.Where(x => !activeBindings.Contains((int)x)).ToList();
                        var or = ctrl.addOrGateForInputs((int)inverse[0], (int)inverse[1], outfit);
                        var not = ctrl.addNotForInput(or.index, outfit);
                        not.setPosition(boundNode.getPosition() - new Vector2(not.getSize().x + perGateOffset, 0));
                        or.setPosition(not.getPosition() - new Vector2(or.getSize().x + perGateOffset, 0));
                        boundNode.SetInput(not.index, 0);
                    } else {
                        // The off state isn't bound, so we can simply or together the two bound states
                        var or = ctrl.addOrGateForInputs(activeBindings[0], activeBindings[1], outfit);
                        or.setPosition(boundNode.getPosition() - new Vector2(or.getSize().x + perGateOffset, 0));
                        boundNode.SetInput(or.index, 0);
                    }
                }

                LogicFlowNode_NOT makeNone(out List<LogicFlowGate> seqNodes) {
                    seqNodes = new List<LogicFlowGate>();
                    List<int> keys = bindKeys.Select(x => (int)x).ToList();
                    if (keys.Count == 1) {
                        var not = ctrl.addNotForInput(keys[0], outfit);
                        seqNodes.Add(not);
                        return not;
                    } else if (keys.Count == 2) {
                        var or = ctrl.addOrGateForInputs(keys[1], keys[0], outfit);
                        seqNodes.Add(or);
                        var not = ctrl.addNotForInput(or.index, outfit);
                        seqNodes.Add(not);
                        return not;
                    } else {
                        var or1 = ctrl.addOrGateForInputs(keys[1], keys[0], outfit);
                        seqNodes.Add(or1);
                        var or2 = ctrl.addOrGateForInputs(or1.index, keys[2], outfit);
                        seqNodes.Add(or2);
                        var not = ctrl.addNotForInput(or2.index, outfit);
                        seqNodes.Add(not);
                        return not;
                    }
                }
            }
        }
    }

    [MessagePackObject]
    public class SerialisedGraphData {
        [Key("Outfit")]
        public int outfit { get; set; }
        [Key("Advanced")]
        public bool advanced { get; set; }
        [Key("Bindings")]
        public Dictionary<int, BindingType> bindings { get; set; }
        [Key("BoundStates")]
        public Dictionary<int, byte> activeBoundStates { get; set; }

        public SerialisedGraphData Serialise(int coord, GraphData gd) {
            var _bindings = new Dictionary<int, BindingType>();
            var _activeBoundStates = new Dictionary<int, byte>();
            foreach (int key in gd.bindings.Keys) {
                if (gd.bindings[key] != null) {
                    _bindings[key] = gd.bindings[key].Value;
                    _activeBoundStates[key] = gd.activeBoundStates[key];
                }
            }
            return new SerialisedGraphData() {
                outfit = coord,
                advanced = gd.advanced,
                bindings = _bindings,
                activeBoundStates = _activeBoundStates,
            };
        }
    }

    public enum BindingType {
        Top,
        Bottom,
        Bra,
        Underwear,
        Gloves,
        Pantyhose,
        Legwear,
        Shoes,
    }
}
