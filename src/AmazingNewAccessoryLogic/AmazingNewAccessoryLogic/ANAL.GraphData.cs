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
                    grp.setPosition(pos - new Vector2(grp.rect.width + 30, 0));
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
                var zInputs = bindingStates[kvp.Value.Value];
                if (activeBindings.Count == 0 || activeBindings.Count == zInputs.Count + 1) continue;

                // Add connections
                if (activeBindings.Count == 1) {
                    if (activeBindings[0] == 0) {
                        var not = makeNone();
                        if (not == null) continue;
                        boundNode.SetInput(not.index, 0);
                    } else {
                        var node = graph.getNodeAt(activeBindings[0]);
                        if (node == null) continue;
                        boundNode.SetInput(node.index, 0);
                    }
                } else if (activeBindings.Count == 2) {
                    if (activeBindings.Contains(0)) {
                        int other = activeBindings.Max();
                        if (zInputs.Count == 2) {
                            int inverse = (int)((int)zInputs[0] == other ? zInputs[1] : zInputs[0]);
                            var not = ctrl.addNotForInput(inverse, outfit);
                            not.setPosition(boundNode.getPosition() - new Vector2(not.getSize().x + 10f, 0));
                            boundNode.SetInput(not.index, 0);
                        } else {
                            var none = makeNone(70f);
                            if (none == null) continue;
                            var or = ctrl.addOrGateForInputs(none.index, other, outfit);
                            or.setPosition(boundNode.getPosition() - new Vector2(or.getSize().x + 10f, 0));
                            boundNode.SetInput(or.index, 0);
                        }
                    } else {
                        var or = ctrl.addOrGateForInputs(activeBindings[0], activeBindings[1], outfit);
                        or.setPosition(boundNode.getPosition() - new Vector2(or.getSize().x + 10f, 0));
                        boundNode.SetInput(or.index, 0);
                    }
                } else if (activeBindings.Count == 3) {
                    var or1 = ctrl.addOrGateForInputs(activeBindings[1], activeBindings[0], outfit);
                    var or2 = ctrl.addOrGateForInputs(or1.index, activeBindings[2], outfit);
                    or2.setPosition(boundNode.getPosition() - new Vector2(or2.getSize().x + 10f, 0));
                    or1.setPosition(or2.getPosition() - new Vector2(or1.getSize().x + 10f, 0));
                    boundNode.SetInput(or2.index, 0);
                } else {
                    continue;
                }

                LogicFlowNode_NOT makeNone(float offset = 0f) {
                    switch (zInputs.Count) {
                        case 1:
                            var not1 = ctrl.addNotForInput((int)zInputs[0], outfit);
                            not1.setPosition(boundNode.getPosition() - new Vector2(not1.getSize().x + 10f + offset, 0));
                            return not1;
                        case 2:
                            var or = ctrl.addOrGateForInputs((int)zInputs[1], (int)zInputs[0], outfit);
                            var not2 = ctrl.addNotForInput(or.index, outfit);
                            not2.setPosition(boundNode.getPosition() - new Vector2(not2.getSize().x + 10f + offset, 0));
                            or.setPosition(not2.getPosition() - new Vector2(or.getSize().x + 10f, 0));
                            return not2;
                        case 3:
                            var or1 = ctrl.addOrGateForInputs((int)zInputs[1], (int)zInputs[0], outfit);
                            var or2 = ctrl.addOrGateForInputs(or1.index, (int)zInputs[2], outfit);
                            var not3 = ctrl.addNotForInput(or2.index, outfit);
                            not3.setPosition(boundNode.getPosition() - new Vector2(not3.getSize().x + 10f + offset, 0));
                            or2.setPosition(not3.getPosition() - new Vector2(or2.getSize().x + 10f, 0));
                            or1.setPosition(or2.getPosition() - new Vector2(or1.getSize().x + 10f, 0));
                            return not3;
                        default:
                            return null;
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
