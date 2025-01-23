using ActionGame.Point;
using Illusion.Extensions;
using LogicFlows;
using MessagePack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AmazingNewAccessoryLogic {
    public class GraphData {
        public readonly LogicFlowGraph graph;

        private bool _advanced = false;
        public bool advanced {
            get {
                return _advanced;
            }
            set {
                if (!value && _advanced) {
                    MakeGraph(true);
                }
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
        private Dictionary<int, BindingType?> bindings = new Dictionary<int, BindingType?>();
        private Dictionary<int, byte> activeBoundStates = new Dictionary<int, byte>();

        private List<int> changedNodes = new List<int>();

        private bool makeRunning = false;

        public GraphData(LogicFlowGraph logicGraph, SerialisedGraphData sGD = null) {
            graph = logicGraph;
            advanced = false;
            if (sGD != null) {
                advanced = sGD.advanced;
                var _bindings = new Dictionary<int, BindingType?>();
                foreach (var key in sGD.bindings.Keys) {
                    _bindings[key] = sGD.bindings[key];
                }
                groupChildren = sGD.groupChildren;
                bindings = _bindings;
                activeBoundStates = sGD.activeBoundStates;
            }
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

            changedNodes.Add(childSlot);
            changedNodes.Add(grpIdx);
            MakeLater();
        }

        public bool RemoveChild(int grpIdx, int childSlot) {
            if (!groupChildren.ContainsKey(grpIdx)) return false;

            bool result = groupChildren[grpIdx].Remove(childSlot);
            if (result) {
                graph.getNodeAt(childSlot + 1000000).inputs[0] = null;
            }

            changedNodes.Add(childSlot);
            changedNodes.Add(grpIdx);
            MakeLater();
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

        public BindingType? GetNodeBinding(int idx) {
            if (bindings.TryGetValue(idx, out var binding)) return binding;
            return null;
        }

        public void SetNodeBinding(int idx, BindingType? value) {
            bindings[idx] = value;

            changedNodes.Add(idx);
            MakeLater();
        }

        public bool GetBoundState(int idx, int shift) {
            if (!activeBoundStates.ContainsKey(idx)) {
                return false;
            }
            return (activeBoundStates[idx] & (1 << shift)) > 0;
        }

        public byte GetBoundStates(int idx) {
            if (activeBoundStates.TryGetValue(idx, out var state)) return state;
            return 0;
        }

        public void SetBoundState(int idx, int shift, bool val) {
            if (!activeBoundStates.ContainsKey(idx)) {
                activeBoundStates.Add(idx, 0);
            }
            if (val) {
                activeBoundStates[idx] |= (byte)(1 << shift);
            } else {
                activeBoundStates[idx] &= (byte)~(1 << shift);
            }

            changedNodes.Add(idx);
            MakeLater();
        }
        
        public void SetBoundStates(int idx, byte val) {
            if (!activeBoundStates.ContainsKey(idx)) {
                activeBoundStates.Add(idx, 0);
            }
            activeBoundStates[idx] = val;

            changedNodes.Add(idx);
            MakeLater();
        }

        public List<int> GetGroups() {
            return groupChildren.Keys.ToList();
        }

        public List<int> GetBoundNodes() {
            return bindings.Keys.ToList();
        }

        public void RemoveGroup(int grpIdx) {
            if (groupChildren.TryGetValue(grpIdx, out var children)) {
                foreach (var child in children) {
                    changedNodes.Add(child);
                }
                groupChildren.Remove(grpIdx);
                MakeLater();
            }
        }

        private void MakeLater() {
            if (!makeRunning) {
                makeRunning = true;
                AmazingNewAccessoryLogic.Instance.StartCoroutine(DoMakeLater());
            }

            IEnumerator DoMakeLater() {
                yield return null;
                MakeGraph();
                makeRunning = false;
            }
        }

        public void MakeGraph(bool all = false) {
            // Only make the graph in simple mode
            if (advanced) return;

            graph.isLoading = true;

            // Get control and current outfit
            var ctrl = AnalCharaController.dicGraphToControl[graph];
            int outfit = ctrl.graphs.Keys.FirstOrDefault(x => ctrl.graphs[x] == graph);

            // Get all node indices whose outputs are being used
            Dictionary<int, int> allSources = new Dictionary<int, int>();
            foreach (var node in graph.nodes) {
                foreach (int? idx in node.Value.inputs) {
                    if (idx != null) {
                        if (!allSources.ContainsKey(idx.Value)) {
                            allSources.Add(idx.Value, 1);
                        } else {
                            allSources[idx.Value]++;
                        }
                    }
                }
            }

            // Clear graph of everything we don't want to reuse
            var allGroupChildren = GetAllChildIndices();
            HashSet<int> toRemove = new HashSet<int>();
            foreach (var node in graph.nodes) {
                if (!all && !changedNodes.Contains(node.Value.index < 0 ? node.Value.index : node.Value.index - 1000000)) continue;
                if (allGroupChildren.Contains(node.Value.index)) continue;
                LogicFlowNode current = null;
                List<LogicFlowNode> toCheck = new List<LogicFlowNode> { node.Value };
                while (toCheck.Count > 0) {
                    current = toCheck.Pop();
                    foreach (int? idx in current.inputs) {
                        if (idx == null) continue;
                        var nowNode = graph.getNodeAt(idx.Value);
                        if (nowNode is LogicFlowInput || nowNode is LogicFlowNode_GRP) continue;
                        if (allSources.TryGetValue(nowNode.index, out int usersNum) && usersNum < 2) {
                            toRemove.Add(nowNode.index);
                            toCheck.Add(nowNode);
                        }
                    }
                }
            }
            foreach (int idx in toRemove) {
                if (idx > 999999 && idx < 1001000) {
                    ctrl.activeSlots[outfit].Remove(idx - 1000000);
                }
                graph.RemoveNode(idx);
            }

            // Connect outputs to group nodes
            foreach (var kvp in groupChildren) {
                if (!all && !changedNodes.Contains(kvp.Key)) continue;
                var grp = (LogicFlowNode_GRP)graph.getNodeAt(kvp.Key);
                foreach (var slot in kvp.Value) {
                    var node = ctrl.getOutput(slot, outfit);
                    if (node.inputs[0] != kvp.Key) {
                        node.SetInput(kvp.Key, 0);
                    }
                }
            }

            // Connect clothing states to requisite nodes
            foreach (var kvp in bindings) {
                // Skip unbound, unchanged (if not updating all), and in-group accessories
                if (allGroupChildren.Contains(kvp.Key)) continue;
                if (!all && !changedNodes.Contains(kvp.Key)) continue;
                if (kvp.Value == null) {
                    var node = ctrl.getOutput(kvp.Key, outfit);
                    if (node != null) {
                        node.inputs[0] = null;
                    }
                    continue;
                }

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
                if (activeBindings.Count == 0) {
                    // No bound states, node should always be inactive
                    var not = ctrl.addNotForInput((int)bindKeys[0], outfit);
                    var and = ctrl.addAndGateForInputs((int)bindKeys[0], not.index, outfit);
                    boundNode.SetInput(and.index, 0);
                } else if (activeBindings.Count == 1) {
                    // Only one state is bound
                    if (activeBindings[0] == 0) {
                        // But that state is the Off state, so we have to invert all the rest
                        var none = makeNone();
                        boundNode.SetInput(none.index, 0);
                    } else {
                        // We can bind the single, non-off state directly to the node
                        boundNode.SetInput(activeBindings[0], 0);
                    }
                } else if (activeBindings.Count == bindKeys.Count + 1) {
                    // All possible states are bound, node should always be active
                    var not = ctrl.addNotForInput((int)bindKeys[0], outfit);
                    var or = ctrl.addOrGateForInputs((int)bindKeys[0], not.index, outfit);
                    boundNode.SetInput(or.index, 0);
                } else if (activeBindings.Count == bindKeys.Count) {
                    // All but one state is bound
                    if (activeBindings.Contains(0)) {
                        // The off input is bound, so we can invert the single remaining input
                        int inverse = (int)bindKeys.Where(x => !activeBindings.Contains((int)x)).First();
                        var not = ctrl.addNotForInput(inverse, outfit);
                        boundNode.SetInput(not.index, 0);
                    } else {
                        // The off input isn't bound so we have to OR together either two or three inputs
                        // Because the one input scenario was handled earlier
                        var or1 = ctrl.addOrGateForInputs(activeBindings[1], activeBindings[0], outfit);
                        if (activeBindings.Count > 2) {
                            var or2 = ctrl.addOrGateForInputs(or1.index, activeBindings[2], outfit);
                            boundNode.SetInput(or2.index, 0);
                        } else {
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
                        boundNode.SetInput(not.index, 0);
                    } else {
                        // The off state isn't bound, so we can simply or together the two bound states
                        var or = ctrl.addOrGateForInputs(activeBindings[0], activeBindings[1], outfit);
                        boundNode.SetInput(or.index, 0);
                    }
                }

                LogicFlowNode_NOT makeNone() {
                    List<int> keys = bindKeys.Select(x => (int)x).ToList();
                    if (keys.Count == 1) {
                        var not = ctrl.addNotForInput(keys[0], outfit);
                        return not;
                    } else if (keys.Count == 2) {
                        var or = ctrl.addOrGateForInputs(keys[1], keys[0], outfit);
                        var not = ctrl.addNotForInput(or.index, outfit);
                        return not;
                    } else {
                        var or1 = ctrl.addOrGateForInputs(keys[1], keys[0], outfit);
                        var or2 = ctrl.addOrGateForInputs(or1.index, keys[2], outfit);
                        var not = ctrl.addNotForInput(or2.index, outfit);
                        return not;
                    }
                }
            }

            CleanGraph();

            changedNodes.Clear();
            graph.isLoading = false;
            graph.ForceUpdate();
        }

        private void CleanGraph() {
            var ctrl = AnalCharaController.dicGraphToControl[graph];
            int outfit = ctrl.graphs.Keys.FirstOrDefault(x => ctrl.graphs[x] == graph);

            // Get all node indices whose outputs are being used
            HashSet<int> allSources = new HashSet<int>();
            foreach (var node in graph.nodes) {
                foreach (int? idx in node.Value.inputs) {
                    if (idx != null) allSources.Add(idx.Value);
                }
            }

            // Remove any unconnected nodes
            HashSet<int> toRemove = new HashSet<int>();
            foreach (var node in graph.nodes.Values) {
                if (node is LogicFlowOutput && node.inputAt(0) == null) {
                    toRemove.Add(node.index);
                }
                if (!(node is LogicFlowNode_GRP) && !(node is LogicFlowOutput) && !(node is LogicFlowInput input && !input.deletable) && !allSources.Contains(node.index)) {
                    List<LogicFlowNode> toIterate = new List<LogicFlowNode> { node };
                    while (toIterate.Count > 0) {
                        var current = toIterate.Pop();
                        toRemove.Add(current.index);
                        foreach (int? idx in current.inputs) {
                            if (idx == null) continue;
                            var inputAt = graph.getNodeAt(idx.Value);
                            if (inputAt != null && !(inputAt is LogicFlowInput input2 && !input2.deletable)) {
                                toIterate.Add(inputAt);
                            }
                        }
                    }
                }
            }
            foreach (int idx in toRemove) {
                if (idx > 999999 && idx < 1001000) {
                    ctrl.activeSlots[outfit].Remove(idx - 1000000);
                }
                graph.RemoveNode(idx);
            }

            // Prettify layout
            int numOutputs = 1;
            float perNodeOffset = 20f;
            var allChildren = GetAllChildIndices();
            foreach (var kvp in groupChildren) {
                Vector2 pos = Vector2.zero;
                foreach (int child in kvp.Value) {
                    var output = ctrl.getOutput(child, outfit);
                    output.setPosition(OutputPos(numOutputs));
                    pos += output.getPosition();
                    numOutputs++;
                }
                var grp = graph.getNodeAt(kvp.Key);
                if (kvp.Value.Count > 0) {
                    grp.setPosition(pos / kvp.Value.Count - new Vector2(grp.getSize().x + perNodeOffset + 10f, 0));
                }
                SetChainPos(grp);
            }
            foreach (var slot in bindings) {
                if (slot.Key > -1 && slot.Value != null && !allChildren.Contains(slot.Key)) {
                    var output = ctrl.getOutput(slot.Key, outfit);
                    if (output == null) continue;
                    output.setPosition(OutputPos(numOutputs));
                    SetChainPos(output);
                    numOutputs++;
                }
            }
            Vector2 OutputPos(int num) => new Vector2(
                graph.getSize().x - 80f,
                graph.getSize().y - 50f * num
            );
            void SetChainPos(LogicFlowNode root) {
                if (root == null) return;
                Vector2 previous = root.getPosition();
                List<LogicFlowNode> toCheck = new List<LogicFlowNode> { root.inputAt(0) };
                while (toCheck.Count > 0) {
                    var current = toCheck.Pop();
                    if (current == null || current is LogicFlowInput) continue;
                    current.setPosition(previous - new Vector2(current.getSize().x + perNodeOffset, 0));
                    previous = current.getPosition();
                    for (int i = 0; i < current.inputs.Length; i++) {
                        if (current.inputs[i] == null) continue;
                        toCheck.Add(current.inputAt(i));
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
        [Key("GroupChildren")]
        public Dictionary<int, List<int>> groupChildren { get; set; }
        [Key("Bindings")]
        public Dictionary<int, BindingType> bindings { get; set; }
        [Key("BoundStates")]
        public Dictionary<int, byte> activeBoundStates { get; set; }

        public static SerialisedGraphData Serialise(int coord, GraphData gd) {
            var _groupChildren = new Dictionary<int, List<int>>();
            foreach (int key in gd.GetGroups()) {
                if (gd.TryGetChildren(key, out var children) && children != null && children.Count > 0) {
                    _groupChildren[key] = new List<int>(children);
                }
            }
            var _bindings = new Dictionary<int, BindingType>();
            var _activeBoundStates = new Dictionary<int, byte>();
            foreach (int key in gd.GetBoundNodes()) {
                if (gd.GetNodeBinding(key) != null) {
                    _bindings[key] = gd.GetNodeBinding(key).Value;
                    _activeBoundStates[key] = gd.GetBoundStates(key);
                }
            }
            return new SerialisedGraphData() {
                outfit = coord,
                advanced = gd.advanced,
                groupChildren = _groupChildren,
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
