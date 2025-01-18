using KKAPI;
using System;
using LogicFlows;
using System.Linq;
using KKAPI.Chara;
using MessagePack;
using UnityEngine;
using KKAPI.Maker;
using System.Collections;
using ExtensibleSaveFormat;
using System.Collections.Generic;

namespace AmazingNewAccessoryLogic
{
    class AnalCharaController : CharaCustomFunctionController
    {
        public const int normalInputWindowID = 2233500;
        public const int advancedInputWindowID = 2233511;
        public const int renameWindowID = 2233522;
        public const int groupSelectWindowID = 2233533;

        public LogicFlowGraph lfg { get => getCurrentGraph(); private set => setCurrentGraph(value); }

        internal static Dictionary<LogicFlowGraph, Dictionary<int, List<object>>> serialisationData = new Dictionary<LogicFlowGraph, Dictionary<int, List<object>>>();

        private Dictionary<int, LogicFlowGraph> graphs = new Dictionary<int, LogicFlowGraph>();
        private Dictionary<int, List<int>> activeSlots = new Dictionary<int, List<int>>();

        internal bool displayGraph = false;
        private static Material mat = new Material(Shader.Find("Hidden/Internal-Colored"));

        //render bodge 
        private RenderTexture rTex;
        private Camera rCam;

        private byte[] oldClothStates;

        #region GameEvetns
        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            if (graphs.Count == 0) return;
            PluginData data = new PluginData();
            Dictionary<int, SerialisedGraph> sCharaData = new Dictionary<int, SerialisedGraph>();
            foreach(int outfit in graphs.Keys)
            {
                if (graphs[outfit].getAllNodes().Count == Enum.GetNames(typeof(InputKey)).Length) continue; 
                sCharaData.Add(outfit, SerialisedGraph.Serialise(graphs[outfit]));
            }
            data.data.Add("Graphs", MessagePackSerializer.Serialize(sCharaData));
            data.data.Add("Version", (byte)1);
            SetExtendedData(data);
        }

        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
            base.OnReload(currentGameMode, maintainState);

            graphs.Clear();
            activeSlots.Clear();

            PluginData data = GetExtendedData();
            if (data == null)
            {
                return;
            }

            byte version = 0;
            if (data.data.TryGetValue("Version", out var versionS) && versionS != null)
            {
                version = (byte)versionS;
            }
            if (version == 1)
            {
                Dictionary<int, SerialisedGraph> sGraphs = new Dictionary<int, SerialisedGraph>();
                if (data.data.TryGetValue("Graphs", out var graphsSerialised) && graphsSerialised != null)
                {
                    sGraphs = MessagePackSerializer.Deserialize<Dictionary<int, SerialisedGraph>>((byte[])graphsSerialised);
                    foreach(int outfit in sGraphs.Keys)
                    {
                        deserialiseGraph(outfit, sGraphs[outfit]);
                        AmazingNewAccessoryLogic.Logger.LogDebug($"Loaded Logic Graph for outfit {outfit}");
                    }
                }
            }
        }

        protected override void OnCoordinateBeingSaved(ChaFileCoordinate coordinate)
        {
            base.OnCoordinateBeingSaved(coordinate);

            PluginData data = new PluginData();
            if (!graphs.ContainsKey(ChaControl.fileStatus.coordinateType)) return;
            SerialisedGraph sGraph = SerialisedGraph.Serialise(graphs[ChaControl.fileStatus.coordinateType]);
            data.data.Add("Graph", MessagePackSerializer.Serialize(sGraph));
            data.data.Add("Version", (byte)1);
            SetCoordinateExtendedData(coordinate, data);
        }

        protected override void OnCoordinateBeingLoaded(ChaFileCoordinate coordinate, bool maintainState)
        {
            base.OnCoordinateBeingLoaded(coordinate, maintainState);
            // Maker partial coordinate load fix
            if (KKAPI.Maker.MakerAPI.InsideAndLoaded)
            {
                // return if no accessories are being loaded
                if (GameObject.Find("cosFileControl")?.GetComponentInChildren<ChaCustom.CustomFileWindow>()?.tglCoordeLoadAcs.isOn == false) return;
            }
            graphs.Remove(ChaControl.fileStatus.coordinateType);
            activeSlots.Remove(ChaControl.fileStatus.coordinateType);

            PluginData data = GetCoordinateExtendedData(coordinate);
            if (data == null)
            {
                return;
            }

            byte version = 0;
            if (data.data.TryGetValue("Version", out var versionS) && versionS != null)
            {
                version = (byte)versionS;
            }
            if (version == 1)
            {
                if (data.data.TryGetValue("Graph", out var serialisedGraph) && serialisedGraph != null)
                {
                    SerialisedGraph sGraph = MessagePackSerializer.Deserialize<SerialisedGraph>((byte[])serialisedGraph);
                    if (sGraph != null)
                    {
                        deserialiseGraph(ChaControl.fileStatus.coordinateType, sGraph);
                        AmazingNewAccessoryLogic.Logger.LogDebug($"Loaded Logic Graph for outfit {ChaControl.fileStatus.coordinateType}");
                    }
                }
            }
        }

        internal void AccessoryTransferred(int sourceSlot, int destinationSlot)
        {
            if (!activeSlots.ContainsKey(ChaControl.fileStatus.coordinateType)) return;
            if (activeSlots[ChaControl.fileStatus.coordinateType].Contains(sourceSlot))
            {
                LogicFlowOutput destNode = (LogicFlowOutput)lfg.getNodeAt(1000000 + destinationSlot);
                if (destNode == null) destNode = addOutput(destinationSlot);
                LogicFlowNode sourceNode = (LogicFlowOutput)lfg.getNodeAt(1000000 + sourceSlot);
                destNode.SetInput(sourceNode.inputAt(0).index, 0);
            }
        }

        internal void AccessoriesCopied(int sourceOutift, int destinationOutfit, IEnumerable<int> slots)
        {
            if (!graphs.ContainsKey(sourceOutift)) return;
            if (!graphs.ContainsKey(destinationOutfit))
            {
                createGraph(destinationOutfit);
            }
            graphs[destinationOutfit].isLoading = true;
            foreach(int slot in slots)
            {
                if (!activeSlots[sourceOutift].Contains(slot)) continue;
                LogicFlowOutput sOutput = (LogicFlowOutput)graphs[sourceOutift].getNodeAt(1000000 + slot);
                if (sOutput == null) continue;
                List<int> iTree = sOutput.getInputTree();
                LogicFlowOutput dOutput = (LogicFlowOutput)graphs[destinationOutfit].getNodeAt(1000000 + slot);
                if (dOutput == null) deserialiseNode(destinationOutfit, SerialisedNode.Serialise(graphs[sourceOutift].getNodeAt(1000000 + slot), graphs[sourceOutift]));
                foreach(int index in iTree)
                {
                    LogicFlowNode node = graphs[destinationOutfit].getNodeAt(index);
                    if (node == null) deserialiseNode(destinationOutfit, SerialisedNode.Serialise(graphs[sourceOutift].getNodeAt(index), graphs[sourceOutift]));
                }
            }
            graphs[destinationOutfit].isLoading = false;
        }
        #endregion
        #region Deserialisation
        private void deserialiseGraph(int outfit, SerialisedGraph graph)
        {
            graphs.Add(outfit, new LogicFlowGraph(new Rect(new Vector2(200,200), graph.size)));
            activeSlots[outfit] = new List<int>();
            graphs[outfit].isLoading = true;
            foreach (SerialisedNode node in graph.nodes) deserialiseNode(outfit, node);
            graphs[outfit].isLoading = false;
        }

        private void deserialiseNode(int outfit, SerialisedNode sNode)
        {
            LogicFlowNode node = null;
            switch (sNode.type)
            {
                case SerialisedNode.NodeType.Gate_NOT:
                    node = new LogicFlowNode_NOT(graphs[outfit], key: sNode.index) { label = "NOT", toolTipText = "NOT" };
                    node.SetInput(sNode.data[0], 0);
                    break;
                case SerialisedNode.NodeType.Gate_AND:
                    node = new LogicFlowNode_AND(graphs[outfit], key: sNode.index) { label = "AND", toolTipText = "AND" };
                    node.SetInput(sNode.data[0], 0);
                    node.SetInput(sNode.data[1], 1);
                    break;
                case SerialisedNode.NodeType.Gate_OR:
                    node = new LogicFlowNode_OR(graphs[outfit], key: sNode.index) { label = "OR", toolTipText = "OR" };
                    node.SetInput(sNode.data[0], 0);
                    node.SetInput(sNode.data[1], 1);
                    break;
                case SerialisedNode.NodeType.Gate_XOR:
                    node = new LogicFlowNode_XOR(graphs[outfit], key: sNode.index) { label = "XOR", toolTipText = "XOR" };
                    node.SetInput(sNode.data[0], 0);
                    node.SetInput(sNode.data[1], 1);
                    break;
                case SerialisedNode.NodeType.Input:
                    node = addInput((InputKey)sNode.index, sNode.postion, outfit);
                    break;
                case SerialisedNode.NodeType.Output:
                    node = addOutput(sNode.data[0], outfit);
                    node.SetInput(sNode.data[1], 0);
                    break;
                case SerialisedNode.NodeType.AdvancedInput:
                    node = deserialiseAdvancedInputNode(outfit, sNode, (AdvancedInputType)sNode.data2[0]);
                    break;
            }
            if (node != null)
            {
                node.enabled = sNode.enabled;
                node.setPosition(sNode.postion);
            }
        }

        private LogicFlowNode deserialiseAdvancedInputNode(int outfit, SerialisedNode sNode, AdvancedInputType advancedInputType)
        {
            LogicFlowNode node = null;
            switch (advancedInputType)
            {
                case AdvancedInputType.HandPtn:
                    node = addAdvancedInputHands((bool)sNode.data2[1], (bool)sNode.data2[2], (int)sNode.data2[3], sNode.postion, outfit, sNode.index);
                    break;
                case AdvancedInputType.EyesOpn:
                    node = addAdvancedInputEyeThreshold((bool)sNode.data2[1], (float)sNode.data2[2], sNode.postion, outfit, sNode.index);
                    break;
                case AdvancedInputType.MouthOpn:
                    node = addAdvancedInputMouthThreshold((bool)sNode.data2[1], (float)sNode.data2[2], sNode.postion, outfit, sNode.index);
                    break;
                case AdvancedInputType.EyesPtn:
                    node = addAdvancedInputEyePattern((int)sNode.data2[1], sNode.postion, outfit, sNode.index);
                    break;
                case AdvancedInputType.MouthPtn:
                    node = addAdvancedInputMouthPattern((int)sNode.data2[1],sNode.postion, outfit, sNode.index);
                    break;
                case AdvancedInputType.EyebrowPtn:
                    node = addAdvancedInputEyebrowPattern((int)sNode.data2[1],sNode.postion, outfit,sNode.index);
                    break;
                case AdvancedInputType.Accessory:
                    node = addAdvancedInputAccessory((int)sNode.data2[1], sNode.postion, outfit, sNode.index);
                    break;
            }
            return node;
        }
        #endregion
        #region Graph Construction
        private LogicFlowGraph getCurrentGraph()
        {
            if (!graphs.ContainsKey(ChaControl.fileStatus.coordinateType)) return null;
            return graphs[ChaControl.fileStatus.coordinateType];
        }

        private void setCurrentGraph(LogicFlowGraph g)
        {
            if (graphs.ContainsKey(ChaControl.fileStatus.coordinateType)) graphs[ChaControl.fileStatus.coordinateType] = g;
            else graphs.Add(ChaControl.fileStatus.coordinateType, g);
            if (activeSlots.ContainsKey(ChaControl.fileStatus.coordinateType)) activeSlots[ChaControl.fileStatus.coordinateType].Clear();
            else activeSlots.Add(ChaControl.fileStatus.coordinateType, new List<int>());
        }

        internal LogicFlowGraph createGraph(int? outfit = null)
        {
            if (!outfit.HasValue) outfit = ChaControl.fileStatus.coordinateType;
            if (graphs == null) graphs = new Dictionary<int, LogicFlowGraph>();
            graphs[outfit.Value] = new LogicFlowGraph(new Rect(new Vector2(100,10), new Vector2(500, 900)));
            if (activeSlots == null) activeSlots = new Dictionary<int, List<int>>();
            activeSlots[outfit.Value] = new List<int>();
            float topY = 900;

            int i = 1;
            foreach(InputKey key in Enum.GetValues(typeof(InputKey)))
            {
                addInput(key, new Vector2(10, topY - 50 * i), outfit.Value);
                i++;
            }
            return graphs[outfit.Value];
        }

        internal LogicFlowInput addInput(InputKey key, Vector2 pos, int? outfit = null)
        {
            LogicFlowGraph g;
            if (!outfit.HasValue) g = lfg;
            else g = graphs[outfit.Value];
            if (g == null) return null;

            LogicFlowInput node = null;
            switch ((int)key)
            {
                case 1001:
                    node = new LogicFlowInput_Func(() => getClothState(0, 0), g, (int)key) { label = "Top On"};
                    break;
                case 1002:
                    node = new LogicFlowInput_Func(() => getClothState(0, 1), g, (int)key) { label = "Top ½"};
                    break;
                case 1003:    
                    node = new LogicFlowInput_Func(() => getClothState(1, 0), g, (int)key) { label = "Btm On"};
                    break;
                case 1004:    
                    node = new LogicFlowInput_Func(() => getClothState(1, 1), g, (int)key) { label = "Btm ½"};
                    break;
                case 1005:    
                    node = new LogicFlowInput_Func(() => getClothState(2, 0), g, (int)key) { label = "Bra On"};
                    break;
                case 1006:    
                    node = new LogicFlowInput_Func(() => getClothState(2, 1), g, (int)key) { label = "Bra ½"};
                    break;
                case 1007:    
                    node = new LogicFlowInput_Func(() => getClothState(3, 0), g, (int)key) { label = "UWear On"};
                    break;
                case 1008:    
                    node = new LogicFlowInput_Func(() => getClothState(3, 1), g, (int)key) { label = "UWear ½"};
                    break;
                case 1009:    
                    node = new LogicFlowInput_Func(() => getClothState(3, 2), g, (int)key) { label = "UWear ¼"};
                    break;
                case 1010:    
                    node = new LogicFlowInput_Func(() => getClothState(4, 0), g, (int)key) { label = "Glove On"};
                    break;
                case 1011:    
                    node = new LogicFlowInput_Func(() => getClothState(4, 1), g, (int)key) { label = "Glove ½"};
                    break;
                case 1012:    
                    node = new LogicFlowInput_Func(() => getClothState(4, 2), g, (int)key) { label = "Glove ¼"};
                    break;
                case 1013:    
                    node = new LogicFlowInput_Func(() => getClothState(5, 0), g, (int)key) { label = "PHose On"};
                    break;
                case 1014:    
                    node = new LogicFlowInput_Func(() => getClothState(5, 1), g, (int)key) { label = "PHose ½"};
                    break;
                case 1015:    
                    node = new LogicFlowInput_Func(() => getClothState(5, 2), g, (int)key) { label = "PHose ¼"};
                    break;
                case 1016:    
                    node = new LogicFlowInput_Func(() => getClothState(6, 0), g, (int)key) { label = "LWear On"};
                    break;
#if KKS
                case 1018:
                    node = new LogicFlowInput_Func(() => getClothState(7, 0), g, (int)key) { label = "Shoes On"};
                    break;
#else
                case 1017:
                    node = new LogicFlowInput_Func(() => getClothState(7, 0), g, key: 1017) {label = "Indoor On"};
                    break;
                case 1018:
                    node = new LogicFlowInput_Func(() => getClothState(8, 0), g, key: 1018) {label = "Outdoor On"};
                    break;
#endif          
                default:
                    break;
            }
            if (node != null)
            {
                node.setPosition(pos);
                node.toolTipText = key.ToString();
                node.deletable = false;
            }
            return node;
        }

        /// <summary>
        /// Returns the LogicFlowNode that is the passed ClothingSlot and ClothingState.
        /// If the State does not have a default Input-Node automatically constructs a MetaInput. 
        /// </summary>
        /// <param name="clothingSlot"></param>
        /// <param name="clothingState"></param>
        /// <param name="outfit"></param>
        /// <returns></returns>
        public LogicFlowNode getInput(int clothingSlot, int clothingState, int? outfit = null)
        {
            if (!outfit.HasValue) outfit = ChaControl.fileStatus.coordinateType;
            InputKey? key = GetInputKey(clothingSlot, clothingState);
            if (key.HasValue)
            {
                if (graphs[outfit.Value]?.getNodeAt((int)key.Value) == null) return null;
                return graphs[outfit.Value].getNodeAt((int)key.Value);
            }
            return constructMetaInput(clothingSlot, outfit);
        }

        /// <summary>
        /// Auto constructs a Node which outputs if one or the other state of a clothingSlot is active
        /// </summary>
        /// <param name="clothingSlot"></param>
        /// <param name="outfit"></param>
        /// <returns></returns>
        public LogicFlowNode_NOT constructMetaInput(int clothingSlot, int? outfit = null)
        {
            if (!outfit.HasValue) outfit = ChaControl.fileStatus.coordinateType;
            if (!graphs.ContainsKey(outfit.Value)) return null;
            LogicFlowGraph graph = graphs[outfit.Value];
            LogicFlowNode or;
            switch(clothingSlot)
            {
                case 0:
                case 1:
                case 2:
                    or = connectInputs(new List<int> { 0, 1 }, clothingSlot, outfit.Value, graph) ;
                    break;
                case 3:
                case 4:
                case 5:
                    or = connectInputs(new List<int> { 0, 1, 2 }, clothingSlot, outfit.Value, graph);
                    break;
                case 6:
                case 7:
                case 8:
                    or = connectInputs(new List<int> { 0 }, clothingSlot, outfit.Value, graph);
                    break;
                default: return null;
            }

            LogicFlowNode_NOT not = addNotForInput(or.index, outfit.Value);
            not.setPosition(or.getPosition() + new Vector2(75, 0));
            return not;
        }

        /// <summary>
        /// Auto constructs a Or gate connected to the passed inputs
        /// </summary>
        /// <param name="inId1"></param>
        /// <param name="inId2"></param>
        /// <param name="outfit"></param>
        /// <returns></returns>
        private LogicFlowNode_OR addOrGateForInputs(int inId1, int inId2, int outfit)
        {
            if (!graphs.ContainsKey(outfit)) return null;
            LogicFlowNode logicFlowNode = graphs[outfit].getAllNodes().Find(node =>
            {
                if (node is null) return false;
                if (node is LogicFlowNode_OR)
                {
                    LogicFlowNode in1 = node.inputAt(0);
                    LogicFlowNode in2 = node.inputAt(1);
                    if (in1 == null || in2 == null) return false;
                    return (in1.index == inId1 && in2.index == inId2) || (in1.index == inId2 && in2.index == inId1);
                }
                return false;
            });

            if (logicFlowNode != null) return logicFlowNode as LogicFlowNode_OR;
            else
            {
                LogicFlowNode_OR or = addGate(outfit, 2) as LogicFlowNode_OR;
                or.SetInput(inId1, 0);
                or.SetInput(inId2, 1);
                return or;
            }
        }

        /// <summary>
        /// Auto constructs a And gate connected to the passed inputs
        /// </summary>
        /// <param name="inId1"></param>
        /// <param name="inId2"></param>
        /// <param name="outfit"></param>
        /// <returns></returns>
        private LogicFlowNode_AND addAndGateForInputs(int inId1, int inId2, int outfit)
        {
            if (!graphs.ContainsKey(outfit)) return null;
            LogicFlowNode logicFlowNode = graphs[outfit].getAllNodes().Find(node =>
            {
                if (node is null) return false;
                if (node is LogicFlowNode_AND)
                {
                    LogicFlowNode in1 = node.inputAt(0);
                    LogicFlowNode in2 = node.inputAt(1);
                    if (in1 == null || in2 == null) return false;
                    return (in1.index == inId1 && in2.index == inId2) || (in1.index == inId2 && in2.index == inId1);
                }
                return false;
            });

            if (logicFlowNode != null) return logicFlowNode as LogicFlowNode_AND;
            else
            {
                LogicFlowNode_AND and = addGate(outfit, 1) as LogicFlowNode_AND;
                and.SetInput(inId1, 0);
                and.SetInput(inId2, 1);
                return and;
            }
        }

        /// <summary>
        /// Auto constructs a Not gate connected to the passed input
        /// </summary>
        /// <param name="inId"></param>
        /// <param name="outfit"></param>
        /// <returns></returns>
        private LogicFlowNode_NOT addNotForInput(int inId, int outfit)
        {
            if (!graphs.ContainsKey(outfit)) return null;
            LogicFlowNode logicFlowNode = graphs[outfit].getAllNodes().Find(node => node != null && node is LogicFlowNode_NOT && node.inputAt(0) != null && node.inputAt(0).index == inId);

            if (logicFlowNode != null) return logicFlowNode as LogicFlowNode_NOT;
            else
            {
                LogicFlowNode_NOT not = addGate(outfit, 0) as LogicFlowNode_NOT;
                not.SetInput(inId, 0);
                return not;
            }
        }

        /// <summary>
        /// Autoconstructs nodes that give an output that turns on for the given states on the given clothingslot.
        /// </summary>
        /// <param name="statesThatTurnTheOutputOn">List of States (0-3) that should turn the output on.</param>
        /// <param name="clothingSlot"></param>
        /// <param name="outfit"></param>
        /// <param name="graph"></param>
        /// <returns></returns>
        private LogicFlowNode connectInputs(List<int> statesThatTurnTheOutputOn, int clothingSlot, int outfit, LogicFlowGraph graph)
        {
            switch (statesThatTurnTheOutputOn.Count)
            {
                case 0:
                    return null;
                case 1:
                    LogicFlowNode input = getInput(clothingSlot, statesThatTurnTheOutputOn[0], outfit);
                    return input;
                case 2:
                    LogicFlowNode input1 = getInput(clothingSlot, statesThatTurnTheOutputOn[0], outfit);
                    LogicFlowNode input2 = getInput(clothingSlot, statesThatTurnTheOutputOn[1], outfit);
                    LogicFlowNode_OR or0 = addOrGateForInputs(input1.index, input2.index, outfit);
                    or0.setPosition(input1.getPosition() + new Vector2(75, 0));
                    return or0;
                case 3:
                    LogicFlowNode input_1 = getInput(clothingSlot, statesThatTurnTheOutputOn[0], outfit);
                    LogicFlowNode input_2 = getInput(clothingSlot, statesThatTurnTheOutputOn[1], outfit);
                    LogicFlowNode input_3 = getInput(clothingSlot, statesThatTurnTheOutputOn[2], outfit);

                    LogicFlowNode_OR or1 = addOrGateForInputs(input_1.index, input_2.index, outfit);
                    LogicFlowNode_OR or2 = addOrGateForInputs(or1.index, input_3.index, outfit);

                    or1.setPosition(input_1.getPosition() + new Vector2(75, 0));

                    or2.setPosition(or1.getPosition() + new Vector2(75, 0));
                    return or2;
                case 4:
                    return null;
                default:
                    return null;
            }
        }

        #region Advanced Inputs

        public LogicFlowNode addAdvancedInputAccessory(int slot, Vector2 pos, int? outfit = null, int? index = null)
        {
            LogicFlowGraph g;
            if (!outfit.HasValue) g = lfg;
            else g = graphs[outfit.Value];
            if (g == null) return null;

            LogicFlowInput_Func node = new LogicFlowInput_Func(() => {
                return ChaControl.fileStatus.showAccessory.Length > slot ? ChaControl.fileStatus.showAccessory[slot] : false; 
            }, g, index) { label = $"Slot {slot+1}" };

            if (node != null)
            {
                node.setPosition(pos);
                node.toolTipText = $"Accessory Slot {slot+1}";
                node.deletable = true;
            }

            if (!serialisationData.ContainsKey(g)) serialisationData.Add(g, new Dictionary<int, List<object>>());
            serialisationData[g].Add(node.index, new List<object> { AdvancedInputType.Accessory, slot });

            return node;
        }

        public LogicFlowNode addAdvancedInputHands(bool leftright, bool anim, int pattern, Vector2 pos, int? outfit = null, int? index = null)
        {
            LogicFlowGraph g;
            if (!outfit.HasValue) g = lfg;
            else g = graphs[outfit.Value];
            if (g == null) return null;

            int sideKey = leftright ? 1 : 0;
            LogicFlowInput_Func node;
            if (anim)
            {
                node = new LogicFlowInput_Func(() => !ChaControl.GetEnableShapeHand(sideKey), g, index) { label = leftright ? "R Hand" : "L Hand" };
            }
            else
            {
                node = new LogicFlowInput_Func(() => ChaControl.GetEnableShapeHand(sideKey) && ChaControl.GetShapeHandIndex(sideKey, 0) == pattern, g, index) { label = leftright ? "Hand-R" : "Hand-L" };
            }

            if (node != null)
            {
                string side = leftright ? "right" : "left";
                string option = anim ? "Animation" : $"Pattern {pattern+1}";

                node.setPosition(pos);
                node.toolTipText = $"Hand: {side} - {option}";
                node.deletable = true;
            }

            if (!serialisationData.ContainsKey(g)) serialisationData.Add(g, new Dictionary<int, List<object>>());
            serialisationData[g].Add(node.index, new List<object> { AdvancedInputType.HandPtn, leftright, anim, pattern });

            return node;
        }

        public LogicFlowNode addAdvancedInputEyeThreshold(bool moreThan, float threshold, Vector2 pos, int? outfit = null, int? index = null)
        {
            LogicFlowGraph g;
            if (!outfit.HasValue) g = lfg;
            else g = graphs[outfit.Value];
            if (g == null) return null;

            LogicFlowInput_Func node;
            if (moreThan)
            {
                node = new LogicFlowInput_Func(() => ChaControl.GetEyesOpenMax() > threshold, g, index) { label = "EyeOpen" };
            }
            else
            {
                node = new LogicFlowInput_Func(() => ChaControl.GetEyesOpenMax() <= threshold, g, index) { label = "EyeOpen" };
            }

            if (node != null)
            {
                string comp = moreThan ? "More than" : "Less or equal to";
                string value = threshold.ToString("0.00");

                node.setPosition(pos);
                node.toolTipText = $"Eye Openess: {comp} {value}";
                node.deletable = true;
            }

            if (!serialisationData.ContainsKey(g)) serialisationData.Add(g, new Dictionary<int, List<object>>());
            serialisationData[g].Add(node.index, new List<object> { AdvancedInputType.EyesOpn, moreThan, threshold });

            return node;
        }

        public LogicFlowNode addAdvancedInputEyePattern(int pattern, Vector2 pos, int? outfit = null, int? index = null)
        {
            LogicFlowGraph g;
            if (!outfit.HasValue) g = lfg;
            else g = graphs[outfit.Value];
            if (g == null) return null;

            LogicFlowInput_Func node = new LogicFlowInput_Func(() => ChaControl.GetEyesPtn() == pattern, g, index) { label = "Eye Ptn" };

            if (node != null)
            {
                node.setPosition(pos);
                node.toolTipText = $"Eye Pattern: {pattern+1}";
                node.deletable = true;
            }

            if (!serialisationData.ContainsKey(g)) serialisationData.Add(g, new Dictionary<int, List<object>>());
            serialisationData[g].Add(node.index, new List<object> { AdvancedInputType.EyesPtn, pattern });

            return node;
        }

        public LogicFlowNode addAdvancedInputMouthThreshold(bool moreThan, float threshold, Vector2 pos, int? outfit = null, int? index = null)
        {
            LogicFlowGraph g;
            if (!outfit.HasValue) g = lfg;
            else g = graphs[outfit.Value];
            if (g == null) return null;

            LogicFlowInput_Func node;
            if (moreThan)
            {
                node = new LogicFlowInput_Func(() => ChaControl.GetMouthOpenMax() > threshold, g, index) { label = "MouthOpn" };
            }
            else
            {
                node = new LogicFlowInput_Func(() => ChaControl.GetMouthOpenMax() <= threshold, g, index) { label = "MouthOpn" };
            }

            if (node != null)
            {
                string comp = moreThan ? "More than" : "Less or equal to";
                string value = threshold.ToString("0.00");

                node.setPosition(pos);
                node.toolTipText = $"Mouth Openess: {comp} {value}";
                node.deletable = true;
            }

            if (!serialisationData.ContainsKey(g)) serialisationData.Add(g, new Dictionary<int, List<object>>());
            serialisationData[g].Add(node.index, new List<object> { AdvancedInputType.MouthOpn, moreThan, threshold });

            return node;
        }

        public LogicFlowNode addAdvancedInputMouthPattern(int pattern, Vector2 pos, int? outfit = null, int? index = null)
        {
            LogicFlowGraph g;
            if (!outfit.HasValue) g = lfg;
            else g = graphs[outfit.Value];
            if (g == null) return null;

            LogicFlowInput_Func node = new LogicFlowInput_Func(() => ChaControl.GetMouthPtn() == pattern, g, index) { label = "MouthPtn" };

            if (node != null)
            {
                node.setPosition(pos);
                node.toolTipText = $"Mouth Pattern: {pattern+1}";
                node.deletable = true;
            }

            if (!serialisationData.ContainsKey(g)) serialisationData.Add(g, new Dictionary<int, List<object>>());
            serialisationData[g].Add(node.index, new List<object> { AdvancedInputType.MouthPtn, pattern });

            return node;
        }

        public LogicFlowNode addAdvancedInputEyebrowPattern(int pattern, Vector2 pos, int? outfit = null, int? index = null)
        {
            LogicFlowGraph g;
            if (!outfit.HasValue) g = lfg;
            else g = graphs[outfit.Value];
            if (g == null) return null;

            LogicFlowInput_Func node = new LogicFlowInput_Func(() => ChaControl.GetEyebrowPtn() == pattern, g, index) { label = "Eyebrow" };

            if (node != null)
            {
                node.setPosition(pos);
                node.toolTipText = $"Eyebrow Pattern: {pattern + 1}";
                node.deletable = true;
            }

            if (!serialisationData.ContainsKey(g)) serialisationData.Add(g, new Dictionary<int, List<object>>());
            serialisationData[g].Add(node.index, new List<object> { AdvancedInputType.EyebrowPtn, pattern });

            return node;
        }


        #endregion
        /// <summary>
        /// Adds a ouput node for an accessory slot 
        /// </summary>
        /// <param name="slot">Accessory Slot</param>
        /// <param name="outfit">Outfit Slot</param>
        public LogicFlowOutput addOutput(int slot, int? outfit = null)
        {
            if (!outfit.HasValue) outfit = ChaControl.fileStatus.coordinateType;

            if (graphs.ContainsKey(outfit.Value))
            {
                if (activeSlots.ContainsKey(outfit.Value) && activeSlots[outfit.Value].Contains(slot)) return null;
                activeSlots[outfit.Value].Add(slot);
                LogicFlowOutput output = new LogicFlowOutput_Action((value) => setAccessoryState(slot, value), graphs[outfit.Value], key: 1000000 + slot) { label = $"Slot {slot + 1}", toolTipText = null };
                output.setPosition(new Vector2(
                    graphs[outfit.Value].getSize().x - 80,
                    graphs[outfit.Value].getSize().y - 50 * (activeSlots[outfit.Value].Count))
                );
                output.nodeDeletedEvent += (object sender, NodeDeletedEventArgs e) =>
                {
                    AmazingNewAccessoryLogic.Logger.LogInfo($"Removed Slot {slot} on outfit {outfit.Value}");
                    activeSlots[outfit.Value].Remove(slot);
                };
                return output;
            }
            return null;
        }

        public LogicFlowOutput getOutput(int slot, int? outfit = null)
        {
            if (!outfit.HasValue) outfit = ChaControl.fileStatus.coordinateType;
            if ( graphs.TryGetValue(outfit.Value, out LogicFlowGraph graph) && graph != null)
            {
                if (graph.getNodeAt(1000000 + slot) == null) return null;
                return graph.getNodeAt(1000000 + slot) as LogicFlowOutput;
            }
            return null;
        }

        /// <summary>
        /// Add a logic gate node to the current graph
        /// </summary>
        /// <param name="type">0 = NOT, 1 = AND, 2 = OR, 3 = XOR, 4 = GRP</param>
        public LogicFlowGate addGate(byte type)
        {
            return addGate(ChaControl.fileStatus.coordinateType, type);
        }

        /// <summary>
        /// Add a logic gate node to the specifed graph
        /// </summary>
        /// <param name="outfit">Outfit Slot</param>
        /// <param name="type">0 = NOT, 1 = AND, 2 = OR, 3 = XOR, 4 = GRP</param>
        public LogicFlowGate addGate(int outfit, byte type)
        {
            if (graphs.ContainsKey(outfit))
            {
                LogicFlowGate gate;
                switch (type)
                {
                    default:
                        gate = new LogicFlowNode_NOT(graphs[outfit]) { label = "NOT", toolTipText = "NOT" };
                        break;
                    case 1:
                        gate = new LogicFlowNode_AND(graphs[outfit]) { label = "AND", toolTipText = "AND" };
                        break;
                    case 2:
                        gate = new LogicFlowNode_OR(graphs[outfit]) { label = "OR", toolTipText = "OR" };
                        break;
                    case 3:
                        gate = new LogicFlowNode_XOR(graphs[outfit]) { label = "XOR", toolTipText = "XOR" };
                        break;
                    case 4:
                        gate = new LogicFlowNode_GRP(graphs[outfit]);
                        break;
                }
                gate.setPosition(graphs[outfit].getSize()/2 - (gate.getSize() / 2));

                return gate;
            }
            return null;
        }
        #endregion
        #region General Methods

        public bool getClothState(int clothType, byte stateValue)
        {
            return ChaControl.fileStatus.clothesState[clothType] == stateValue;
        }

        public void setAccessoryState(int accessorySlot, bool stateValue)
        {
            if (accessorySlot > ChaControl.fileStatus.showAccessory.Length) return;
            ChaControl.fileStatus.showAccessory[accessorySlot] = stateValue;
        }

        public void Show(bool resetPostion)
        {
            if (lfg == null) createGraph();
            displayGraph = true;
            AnalCameraComponent acc = rCam.GetOrAddComponent<AnalCameraComponent>();
            acc.OnPostRenderEvent += postRenderEvent;
            lfg.setUIScaleModifier(AmazingNewAccessoryLogic.UIScaleModifier.Value);
            GameCursor c = GameCursor.Instance;
            if (resetPostion)
            {
                lfg.setPosition(new Vector2(100,10));
            }
            if (c != null)
            {
                c.SetCursorLock(false);
            }
        }

        public void Hide()
        {
            displayGraph = false;
            if (MakerAPI.InsideAndLoaded) AmazingNewAccessoryLogic.SidebarToggle.Value = false;
            AnalCameraComponent acc = rCam.GetComponent<AnalCameraComponent>();
            if (acc != null) acc.OnPostRenderEvent -= postRenderEvent;
        }

        private void postRenderEvent(object sender, CameraEventArgs e)
        {
            if (lfg == null) return;
            if (displayGraph)
            {
                // init GL
                GL.PushMatrix();
                mat.SetPass(0);
                GL.LoadOrtho();

                lfg.draw();

                // end GL
                GL.PopMatrix();
            }

        }

        protected override void Start()
        {
            rTex = new RenderTexture(Screen.width, Screen.height, 32);
            // Create a new camera
            GameObject renderCameraObject = new GameObject("ANAL_UI_Camera");
            renderCameraObject.transform.SetParent(this.transform);
            rCam = renderCameraObject.AddComponent<Camera>();
            rCam.targetTexture = rTex;
            rCam.clearFlags = CameraClearFlags.SolidColor;
            rCam.backgroundColor = Color.clear;
            rCam.cullingMask = 0;

            oldClothStates = (byte[])ChaControl.fileStatus.clothesState.Clone();
            

            base.Start();
        }

        protected override void Update()
        {
            if (lfg == null)
            {
                if (MakerAPI.InsideAndLoaded) AmazingNewAccessoryLogic.SidebarToggle.Value = false;
                return;
            }
            if (displayGraph)
            {
                lfg.update();
                if (MakerAPI.InsideAndLoaded)
                {
                    if (lfg.eatingInput 
                        && AmazingNewAccessoryLogic.Instance.getMakerCursorMangaer() != null 
                        && AmazingNewAccessoryLogic.Instance.getMakerCursorMangaer().isActiveAndEnabled == true)
                    {
                        AmazingNewAccessoryLogic.Instance.getMakerCursorMangaer().enabled = false;
                    }
                    if (!lfg.eatingInput 
                        && AmazingNewAccessoryLogic.Instance.getMakerCursorMangaer() != null 
                        && AmazingNewAccessoryLogic.Instance.getMakerCursorMangaer().isActiveAndEnabled == false)
                    {
                        AmazingNewAccessoryLogic.Instance.getMakerCursorMangaer().enabled = true;
                    }
                }
            }
            else lfg.backgroundUpdate();

            if(!Enumerable.SequenceEqual(oldClothStates, ChaControl.fileStatus.clothesState))
            {
                lfg.ForceUpdate();
            }
            oldClothStates = (byte[])ChaControl.fileStatus.clothesState.Clone();

            base.Update();
        }

        #region OnGUI
        private Rect normalInputRect = new Rect(0, 0, 130, 20);

#if KKS
        private bool kkcompatibility = false;
#endif
        private bool fullCharacter = false;
        private string studioAddOutputTextInput = "1";
        private bool showHelp = false;
        private Vector2 showHelpScroll = new Vector2();

        private List<string> helpText = new List<string>()
        {
            "Basics:",
            "Connect nodes by dragging from the right triangle (output) to left triangle (input) of another\n"+
            "Disconnect nodes dragging the connection away from the input or by RIGHT CLICKING the input\n"+
            "RED nodes/Connections means its currently OFF\n"+
            "GREEN nodes/Connections means its currenlty ON\n"+
            "Nodes with RED BORDER have missing inputs\n"+
            "Nodes with a YELLOW body are currently selected",
            "Controls:",
            "Move Nodes by CLICKING and DRAGGING them\n"+
            "Resize the window on its BOTTOM RIGHT corner\n"+
            "Select MULTIPLE nodes by holding SHIFT\n"+
            "Select a group of nodes with a box (left drag on empty space)\n"+
            "Unselect all nodes by left clicking empty space\n"+
            "Delete selected nodes by pressing DEL\n"+
            "Disable selected nodes by pressing ALT+D\n"+
            "Disabled nodes will output FALSE, no matter the input\n"+
            "Select all downstream nodes of the selected nodes by pressing T (Tree)\n"+
            "Select all influenced nodes of the selected nodes by pressing N (Network)",
            "Basic Nodes:",
            "INPUTS turn on/off according to the clothing state they represent\n"+
            "ACCESSORY INPUTS turn on/off according to the accessory slot they represent\n"+
            "Add ACCESSORY INPUTS by clicking the button in the accessory UI\n"+
            "OUTPUTS control the according accessory slot\n"+
            "Add OUTPUTs by clicking the button in the accessory UI\n"+
            "NOT-GATES output the opposite of their input\n"+
            "AND-GATES turn on if BOTH inputs are on\n"+
            "OR-GATES turn on if ONE OR BOTH inputs are on\n"+
            "XOR-GATES turn on if EXACTLY ONE input is on",
            "Advanced Input Nodes:",
            "HAND PATTERN is on if the specified hand is set to the specified pattern\n"+
            "EYE PATTERN is on if the eyes are set to the specified pattern\n"+
            "EYE THRESHOLD is on if the eye are MORE or LESS OR EQUALLY open compared to the specified threshold\n"+
            "MOUTH PATTERN is on if the moth is set to the specified pattern\n"+
            "MOUTH THRESHOLD is on if the mouth is MORE or LESS OR EQUALLY open compared to the specified theshold\n"+
            "EYEBROW PATTERN is on if the eyesbrows are set to the specified pattern",
            "ASS Data Conversion",
            "Feature is experimental, no guarantees!!\n"+
            "Tries to convert Accessory State Sync data saved in the card to a ANAL graph\n"+
            "Only Accessories with ONE or TWO connected clothing slots are supported\n"+
            "The generated nodes will not be sorted properly and overlap\n"+
            "The generated graph can often be simplified a lot"
        };

        private int? renamedNode = null;
        private Rect renameRect = new Rect();
        private string renameName = "";

        internal LogicFlowNode_GRP groupToSetActives = null;
        private List<LogicFlowNode> groupConnections = null;
        private Rect groupScrollRect = new Rect();
        private Vector2 groupScrollPos = Vector2.zero;

        void OnGUI()
        {
            
            if (lfg == null) return;
            if (displayGraph)
            {
                var solidSkin = KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin;

                if (showAdvancedInputWindow)
                {
                    advancedInputWindowRect = GUI.Window(advancedInputWindowID, advancedInputWindowRect, CustomInputWindowFunction, "", KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.window);
                }
                GUIStyle headerTextStyle = new GUIStyle(GUI.skin.label);
                headerTextStyle.normal.textColor = Color.black;
                headerTextStyle.alignment = TextAnchor.MiddleLeft;
                if (lfg.getUIScale() < 1f)
                {
                    headerTextStyle.fontSize = (int)(14 * lfg.getUIScale());
                    headerTextStyle.padding.top = 0;
                    headerTextStyle.padding.bottom = 0;
                    headerTextStyle.padding.left = 1;
                }      

                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), rTex);

                GUI.Label(new Rect(screenToGUI(lfg.positionUI + new Vector2(10, lfg.sizeUI.y + (lfg.getUIScale() * 20) + 15)), new Vector2(250, 25)), $"AmazingNewAccessoryLogic v{AmazingNewAccessoryLogic.Version}", headerTextStyle);
                if (GUI.Button(new Rect(screenToGUI(lfg.positionUI + lfg.sizeUI + new Vector2(-65, (lfg.getUIScale() * 28) + 4)), new Vector2(60, (lfg.getUIScale() * 10) +10)), "Close")) {
                    Hide();
                }

                normalInputRect.position = screenToGUI(lfg.positionUI + lfg.sizeUI + new Vector2(5, 0));
                normalInputRect = GUILayout.Window(normalInputWindowID, normalInputRect, (x) => {
                    GUILayout.BeginVertical();

                    // add nodes buttons
                    if (GUILayout.Button("Add NOT Gate", GUILayout.Height(30))) addGate(0);
                    if (GUILayout.Button("Add AND Gate", GUILayout.Height(30))) addGate(1);
                    if (GUILayout.Button("Add OR Gate", GUILayout.Height(30))) addGate(2);
                    if (GUILayout.Button("Add XOR Gate", GUILayout.Height(30))) addGate(3);
                    if (GUILayout.Button("Add Group Node", GUILayout.Height(30))) addGate(4);
                    GUILayout.Space(8);
                    if (GUILayout.Button("Advanced Inputs", GUILayout.Height(30))) showAdvancedInputWindow = !showAdvancedInputWindow;
                    GUILayout.Space(8);
#if KKS
                    kkcompatibility = GUILayout.Toggle(kkcompatibility, "KK Compatiblity");
#endif
                    if (GUILayout.Button(fullCharacter ? "◀ All Outfits ▶" : "◀ Current Outfit ▶")) fullCharacter = !fullCharacter;
                    if (GUILayout.Button("Load from ASS"))
                    {
                        if (fullCharacter) TranslateFromAssForCharacter();
                        else if (ExtendedSave.GetExtendedDataById(ChaControl.nowCoordinate, "madevil.kk.ass") == null) TranslateFromAssForCharacter(ChaControl.fileStatus.coordinateType);
                        else TranslateFromAssForCoordinate();
                    }
                    GUILayout.Space(8);
                    if (GUILayout.Button("Show Help")) showHelp = !showHelp;
                    GUILayout.Space(8);
                
                    #region Studio Output Widget
                    if (KKAPI.Studio.StudioAPI.InsideStudio)
                    {
                        GUILayout.BeginHorizontal();
                        studioAddOutputTextInput = GUILayout.TextField(studioAddOutputTextInput);
                        if (GUILayout.Button("+", GUILayout.Width(25)) && int.TryParse(studioAddOutputTextInput, out int a)) studioAddOutputTextInput = (a + 1).ToString();
                        if (GUILayout.Button("-", GUILayout.Width(25)) && int.TryParse(studioAddOutputTextInput, out int b) && b > 1) studioAddOutputTextInput = (b - 1).ToString();
                        GUILayout.EndHorizontal();
                        if (GUILayout.Button("Add Output"))
                        {
                            if (int.TryParse(studioAddOutputTextInput, out int slot) && slot >= 1)
                            {
                                addOutput(slot - 1);
                            }
                        }
                    }
                    #endregion
                    GUILayout.EndVertical();
                }, "", solidSkin.window, GUILayout.ExpandWidth(false));
                KKAPI.Utilities.IMGUIUtils.EatInputInRect(normalInputRect);

                #region HELP
                if (showHelp)
                {
                    GUI.Box(new Rect(screenToGUI(lfg.positionUI + new Vector2(-255, lfg.sizeUI.y)), new Vector2(250, 350)), "HELP TEXT", KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin.window);
                    GUILayout.BeginArea(new Rect(screenToGUI(lfg.positionUI + new Vector2(-255, lfg.sizeUI.y - 20)), new Vector2(250, 330)));
                    showHelpScroll = GUILayout.BeginScrollView(showHelpScroll);
                    GUILayout.BeginVertical();

                    GUIStyle helpLableStyle = new GUIStyle(GUI.skin.box);
                    helpLableStyle.alignment = TextAnchor.MiddleCenter;
                    helpLableStyle.wordWrap = true;

                    foreach (string line in helpText)
                    {
                        GUILayout.Label(line, helpLableStyle);
                    }

                    GUILayout.EndVertical();
                    GUILayout.EndScrollView();
                    GUILayout.EndArea();
                }
                #endregion

                #region Temporary IMGUI Elements
                Vector2 expansion = new Vector2(10, 10);
                // Node renaming
                if (renamedNode != null && !new Rect(renameRect.position - expansion, renameRect.size + 2 * expansion).Contains(Event.current.mousePosition)) {
                    renamedNode = null;
                }
                if (renamedNode != null) {
                    renameRect = GUILayout.Window(renameWindowID, renameRect, (x) => {
                        GUILayout.BeginVertical();
                        renameName = GUILayout.TextArea(renameName);
                        bool entered = false;
                        if (renameName.Contains("\n")) {
                            renameName = renameName.Replace("\n", "").Trim();
                            entered = true;
                        }
                        if (GUILayout.Button("Rename") || entered) {
                            renameName = renameName.Trim();
                            switch (lfg.nodes[renamedNode.GetValueOrDefault()]) {
                                case LogicFlowNode_GRP grp:
                                    grp.setName(renameName);
                                    break;
                                case LogicFlowNode other:
                                    other.label = renameName;
                                    break;
                            }
                            renamedNode = null;
                        }
                        GUILayout.EndVertical();
                    }, $"Renaming '{lfg.nodes[renamedNode.GetValueOrDefault()].label}'", solidSkin.window);
                    GUI.BringWindowToFront(renameWindowID);
                    KKAPI.Utilities.IMGUIUtils.EatInputInRect(renameRect);
                }
                // Group connection selection
                if (groupToSetActives != null && !new Rect(groupScrollRect.position - expansion, groupScrollRect.size + 2 * expansion).Contains(Event.current.mousePosition)) {
                    groupToSetActives = null;
                }
                if (groupToSetActives != null) {
                    groupScrollRect = GUILayout.Window(groupSelectWindowID, groupScrollRect, (x) => {
                        groupScrollPos = GUILayout.BeginScrollView(groupScrollPos, false, true, solidSkin.horizontalScrollbar, solidSkin.verticalScrollbar, GUILayout.Width(240f));
                        foreach (var node in groupConnections) {
                            bool active = groupToSetActives.getActive(node);
                            if (GUILayout.Button((active ? "--- " : "") + node.label + (active ? ": On ---" : ": Off"), solidSkin.button)) {
                                if (active) {
                                    groupToSetActives.removeActiveNode(node);
                                } else {
                                    groupToSetActives.addActiveNode(node);
                                }
                            }
                        }
                        GUILayout.EndScrollView();
                    }, $"Accs for state {groupToSetActives.state}", solidSkin.window);
                    GUI.BringWindowToFront(groupSelectWindowID);
                    KKAPI.Utilities.IMGUIUtils.EatInputInRect(groupScrollRect);
                }
                #endregion

                lfg.ongui();

                #region EVENTS
                Event e = Event.current;
                if (e.isMouse && e.button == 1 && e.type == EventType.MouseUp) {
                    foreach (var kvp in getCurrentGraph().nodes) {
                        { // Renaming
                            if (kvp.Value.mouseOver) {
                                renamedNode = kvp.Key;
                                if (kvp.Value is LogicFlowNode_GRP grp) renameName = grp.getName();
                                else renameName = kvp.Value.label;
                                renameRect = new Rect(e.mousePosition - new Vector2(120, 20), new Vector2(240, 40));
                                break;
                            }
                        }
                        { // Group activation selection
                            if (kvp.Value.outputHovered && kvp.Value is LogicFlowNode_GRP grp) {
                                groupToSetActives = grp;
                                groupScrollRect = new Rect(e.mousePosition - new Vector2(-5, 120), new Vector2(120, 240));
                                groupConnections = getCurrentGraph().nodes.Values.Where(x => x.inputs.Any(y => y == grp.index)).ToList();
                                break;
                            }
                        }
                    }
                }
                if (e.isMouse && (e.button == 0 || e.button == 1) && e.type == EventType.MouseUp) {
                    foreach (var kvp in getCurrentGraph().nodes) {
                        if (kvp.Value.inputHovered != null && kvp.Value is LogicFlowOutput output) {
                            StartCoroutine(updateLater());
                            IEnumerator updateLater() {
                                yield return null;
                                output.forceUpdate();
                            }
                        }
                    }
                }
                #endregion
            }
        }
        #endregion

        #region Advanced Input GUI
        private bool showAdvancedInputWindow = false;
        private Rect advancedInputWindowRect = new Rect(25, 25, 360, 400);
        private Vector2 advanceInputWindowScroll = new Vector2();
        // Hand
        private bool advinpHandSide = false;
        private bool advinpHandAnim = false;
        private string advinpHandPatternText = "1";
        // Eye Open
        private float advinpEyeOpenThreshold = 0.5f;
        private bool advinpEyeOpenThresholdMore = false;
        // Eye Pattern
        private string advinpEyePatternText = "1";
        // Eyebrow pattern
        private string advinpEyebrowPatternText = "1";
        // Mouth Open
        private float advinpMouthOpenThreshold = 0.5f;
        private bool advinpMouthOpenThresholdMore = false;
        // Mouth Pattern
        private string advinpMouthPatternText = "1";

        private void CustomInputWindowFunction(int id)
        {
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.padding = new RectOffset(0, 0, 0, 0);
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            GUIStyle buttonStyleGreen = new GUIStyle(GUI.skin.button);
            buttonStyleGreen.normal.textColor = Color.green;
            GUIStyle buttonStyleRed = new GUIStyle(GUI.skin.button);
            buttonStyleRed.normal.textColor = Color.red;
            GUIStyle labelStyle = new GUIStyle(GUI.skin.box);
            labelStyle.alignment = TextAnchor.MiddleCenter;

            if (GUI.Button(new Rect(advancedInputWindowRect.width - 18, 2, 15, 15), "X", buttonStyle))
            {
                showAdvancedInputWindow = false;
            }
            GUILayout.BeginArea(new Rect(5, 20, advancedInputWindowRect.width-10, advancedInputWindowRect.height-30));
            advanceInputWindowScroll = GUILayout.BeginScrollView(advanceInputWindowScroll);
            GUILayout.BeginVertical();
            

            #region Hand Pattern Input
            GUILayout.Label("Hand Pattern", labelStyle);
            if (GUILayout.Button(advinpHandSide ? "◀ Right ▶" : "◀ Left ▶")) { advinpHandSide = !advinpHandSide; }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(advinpHandAnim ? "◀ Animation ▶" : "◀ Pattern ▶")) { advinpHandAnim = !advinpHandAnim; }
            if (!advinpHandAnim)
            {
                advinpHandPatternText = GUILayout.TextField(advinpHandPatternText, GUILayout.Width(50));
                if (GUILayout.Button("+", GUILayout.Width(25)) && int.TryParse(advinpHandPatternText, out int handPt1)) advinpHandPatternText = (handPt1+1).ToString();
                if (GUILayout.Button("-", GUILayout.Width(25)) && int.TryParse(advinpHandPatternText, out int handPt2) && handPt2 > 1) advinpHandPatternText = (handPt2-1).ToString();
                if (GUILayout.Button("?", GUILayout.Width(25))) advinpHandPatternText = (ChaControl.GetShapeHandIndex(advinpHandSide ? 0 : 1, 0)+1).ToString();
            }
            GUILayout.EndHorizontal();
            bool eval = advinpHandAnim ? !ChaControl.GetEnableShapeHand(advinpHandSide ? 0 : 1) : ChaControl.GetEnableShapeHand(advinpHandSide ? 0 : 1) && int.TryParse(advinpHandPatternText, out int p) && ChaControl.GetShapeHandIndex(advinpHandSide ? 0 : 1, 0) == p - 1;
            if (GUILayout.Button("Add Hand Pattern Input", eval ? buttonStyleGreen : buttonStyleRed))
            {
                int pattern = 1;
                if (advinpHandAnim || (!advinpHandAnim && int.TryParse(advinpHandPatternText, out pattern))) addAdvancedInputHands(advinpHandSide, advinpHandAnim, pattern - 1, lfg.getSize() / 2);
            }
            #endregion

            GUILayout.Space(15);

            #region Eye Open Threshold
            GUILayout.Label("Eye Openess Threshold", labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(advinpEyeOpenThresholdMore ? "◀ More Than ▶" : "◀ Less Than ▶")) { advinpEyeOpenThresholdMore = !advinpEyeOpenThresholdMore; }
            if (GUILayout.Button("?", GUILayout.Width(25))) advinpEyeOpenThreshold = ChaControl.GetEyesOpenMax();
            advinpEyeOpenThreshold = GUILayout.HorizontalSlider(advinpEyeOpenThreshold, 0, 1);
            GUILayout.Label(advinpEyeOpenThreshold.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            eval = advinpEyeOpenThresholdMore ? ChaControl.GetEyesOpenMax() > advinpEyeOpenThreshold : ChaControl.GetEyesOpenMax() <= advinpEyeOpenThreshold;
            if (GUILayout.Button("Add Eye Threshold Input", eval ? buttonStyleGreen : buttonStyleRed))
            {
                addAdvancedInputEyeThreshold(advinpEyeOpenThresholdMore, advinpEyeOpenThreshold, lfg.getSize() / 2);
            }
            #endregion

            GUILayout.Space(15);

            #region Eye Pattern Input
            GUILayout.Label("Eye Pattern", labelStyle);
            GUILayout.BeginHorizontal(); 
            advinpEyePatternText = GUILayout.TextField(advinpEyePatternText);
            if (GUILayout.Button("+", GUILayout.Width(25)) && int.TryParse(advinpEyePatternText, out int eyePt1)) advinpEyePatternText = (eyePt1 + 1).ToString();
            if (GUILayout.Button("-", GUILayout.Width(25)) && int.TryParse(advinpEyePatternText, out int eyePt2) && eyePt2 > 1) advinpEyePatternText = (eyePt2 - 1).ToString();
            if (GUILayout.Button("?", GUILayout.Width(25))) advinpEyePatternText = (ChaControl.GetEyesPtn()+1).ToString();
            GUILayout.EndHorizontal();
            eval = int.TryParse(advinpEyePatternText, out p) && ChaControl.GetEyesPtn() == p - 1;
            if (GUILayout.Button("Add Eye Pattern Input", eval ? buttonStyleGreen : buttonStyleRed) && int.TryParse(advinpEyePatternText, out int pt)) addAdvancedInputEyePattern(pt - 1, lfg.getSize() / 2);
            #endregion

            GUILayout.Space(15);

            #region Eyebrow Pattern Input
            GUILayout.Label("Eyebrow Pattern", labelStyle);
            GUILayout.BeginHorizontal();
            advinpEyebrowPatternText = GUILayout.TextField(advinpEyebrowPatternText);
            if (GUILayout.Button("+", GUILayout.Width(25)) && int.TryParse(advinpEyebrowPatternText, out int eyebPt1)) advinpEyebrowPatternText = (eyebPt1 + 1).ToString();
            if (GUILayout.Button("-", GUILayout.Width(25)) && int.TryParse(advinpEyebrowPatternText, out int eyebPt2) && eyebPt2 > 1) advinpEyebrowPatternText = (eyebPt2 - 1).ToString();
            if (GUILayout.Button("?", GUILayout.Width(25))) advinpEyebrowPatternText = (ChaControl.GetEyebrowPtn() + 1).ToString();
            GUILayout.EndHorizontal();
            eval = int.TryParse(advinpEyebrowPatternText, out p) && ChaControl.GetEyebrowPtn() == p - 1;
            if (GUILayout.Button("Add Eyebrow Pattern Input", eval ? buttonStyleGreen : buttonStyleRed) && int.TryParse(advinpEyebrowPatternText, out pt)) addAdvancedInputEyebrowPattern(pt - 1, lfg.getSize() / 2);
            #endregion

            GUILayout.Space(15);

            #region Mouth Open Threshold
            GUILayout.Label("Mouth Openess Threshold", labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(advinpMouthOpenThresholdMore ? "◀ More Than ▶" : "◀ Less Than ▶")) { advinpMouthOpenThresholdMore = !advinpMouthOpenThresholdMore; }
            if (GUILayout.Button("?", GUILayout.Width(25))) advinpMouthOpenThreshold = ChaControl.GetMouthOpenMax();
            advinpMouthOpenThreshold = GUILayout.HorizontalSlider(advinpMouthOpenThreshold, 0, 1);
            GUILayout.Label(advinpMouthOpenThreshold.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            eval = advinpMouthOpenThresholdMore ? ChaControl.GetMouthOpenMax() > advinpMouthOpenThreshold : ChaControl.GetMouthOpenMax() <= advinpMouthOpenThreshold;
            if (GUILayout.Button("Add Mouth Threshold Input", eval ? buttonStyleGreen : buttonStyleRed))
            {
                addAdvancedInputMouthThreshold(advinpMouthOpenThresholdMore, advinpMouthOpenThreshold, lfg.getSize() / 2);
            }
            #endregion

            GUILayout.Space(15);

            #region Mouth Pattern Input
            GUILayout.Label("Mouth Pattern", labelStyle);
            GUILayout.BeginHorizontal(); 
            advinpMouthPatternText = GUILayout.TextField(advinpMouthPatternText);
            if (GUILayout.Button("+", GUILayout.Width(25)) && int.TryParse(advinpMouthPatternText, out int mouthPt1)) advinpMouthPatternText = (mouthPt1 + 1).ToString();
            if (GUILayout.Button("-", GUILayout.Width(25)) && int.TryParse(advinpMouthPatternText, out int mouthPt2) && mouthPt2 > 1) advinpMouthPatternText = (mouthPt2 - 1).ToString();
            if (GUILayout.Button("?", GUILayout.Width(25))) advinpMouthPatternText = (ChaControl.GetMouthPtn()+1).ToString();
            GUILayout.EndHorizontal();
            eval = int.TryParse(advinpMouthPatternText, out p) && ChaControl.GetMouthPtn() == p - 1;
            if (GUILayout.Button("Add Mouth Pattern Input", eval ? buttonStyleGreen : buttonStyleRed) && int.TryParse(advinpMouthPatternText, out pt)) addAdvancedInputMouthPattern(pt - 1, lfg.getSize() / 2);
            #endregion


            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            advancedInputWindowRect = KKAPI.Utilities.IMGUIUtils.DragResizeEatWindow(id, advancedInputWindowRect);

        }
        #endregion

        private Vector2 screenToGUI(Vector2 screenPos)
        {
            return new Vector2(screenPos.x, Screen.height - screenPos.y);
        }
#if KKS
        // KK -> KKS compatibility switcher
        private int OutfitKK2KKS(int slot)
        {
            switch (slot)
            {
                case 0: return 4;
                case 1: return 3;
                case 2: return 5;
                case 3: return 1;
                case 4: return 6;
                case 5: return 0;
                case 6: return 2;
                default: return slot;
            }
        }
#endif
        public static InputKey? GetInputKey(int clothingSlot, int clothingState)
        {
            switch (clothingSlot)
            {
                case 0:
                    if (clothingState == 0) return InputKey.TopOn;
                    if (clothingState == 1) return InputKey.TopShift;
                    return null;
                case 1:
                    if (clothingState == 0) return InputKey.BottomOn;
                    if (clothingState == 1) return InputKey.BottomShift;
                    return null;
                case 2:
                    if (clothingState == 0) return InputKey.BraOn;
                    if (clothingState == 1) return InputKey.BraShift;
                    return null;
                case 3:
                    if (clothingState == 0) return InputKey.UnderwearOn;
                    if (clothingState == 1) return InputKey.UnderwearShift;
                    if (clothingState == 2) return InputKey.UnderwearHang;
                    return null;
                case 4:
                    if (clothingState == 0) return InputKey.GlovesOn;
                    if (clothingState == 1) return InputKey.GlovesShift;
                    if (clothingState == 2) return InputKey.GlovesShift;
                    return null;
                case 5:
                    if (clothingState == 0) return InputKey.PantyhoseOn;
                    if (clothingState == 1) return InputKey.PantyhoseShift;
                    if (clothingState == 2) return InputKey.PantyhoseHang;
                    return null;
                case 6:
                    if (clothingState == 0) return InputKey.LegwearOn;
                    return null;
#if KK
                case 7:
                    if (clothingState == 0) return InputKey.ShoesIndoorOn;
                    return null;
                case 8:
                    if (clothingState == 0) return InputKey.ShoesOutdoorOn;
                    return null;
#else
                case 8:
                    if (clothingState == 0) return InputKey.ShoesOn;
                    return null;
#endif
                default:
                    return null;
            }
        }
        #endregion
        #region ASS Data tranlation
        /// <summary>
        /// check is this trigger is valid (either existing state or off)
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private bool validTrigger(TriggerProperty t)
        {
            if (t.ClothingState == 3) return true;
            else return GetInputKey(t.ClothingSlot, t.ClothingState).HasValue;
        }

        private void CreateNodesForAssData(Dictionary<int, Dictionary<int, TriggerProperty[]>> data, int outfit)
        {
            AmazingNewAccessoryLogic.Logger.LogInfo($"Creating Logic for {data.Keys.Count} slots on outfit {outfit}");
            if (!graphs.TryGetValue(outfit, out LogicFlowGraph graph)) graph = createGraph(outfit);

            List<int> doneSlots = new List<int>();

            // Translation
            foreach (int slot in data.Keys)
            {
                // clothing slot -> trigger property
                Dictionary<int, TriggerProperty[]> slotData = data[slot];

                AmazingNewAccessoryLogic.Logger.LogDebug($"Converting TriggerProperties for accessory slot {slot}");

                LogicFlowNode output = getOutput(slot, outfit);
                if (output == null)
                {
                    output = addOutput(slot, outfit);
                }

                // no priorities, only one clothing slot
                if (slotData.Keys.Count == 1)
                {
                    int clothingSlot = slotData.Keys.First();
                    TriggerProperty[] trigs = slotData[clothingSlot];
                    List<int> statesThatTurnTheOutputOn = new List<int>();
                    foreach (TriggerProperty trig in trigs)
                    {
                        if (trig.Visible && validTrigger(trig)) statesThatTurnTheOutputOn.Add(trig.ClothingState);
                    }
                    AmazingNewAccessoryLogic.Logger.LogDebug($"Found 1 clothing slot [{clothingSlot}] for acc {slot}: States that turn the ouput on: {statesThatTurnTheOutputOn}");
                    switch (statesThatTurnTheOutputOn.Count)
                    {
                        case 0:
                            break;
                        case 1:
                        case 2:
                        case 3:
                            output.SetInput(connectInputs(statesThatTurnTheOutputOn, clothingSlot, outfit, graph).index, 0);
                            break;
                        case 4:
                            break;
                    }
                }
                if (slotData.Keys.Count == 2)
                {
                    List<TriggerProperty[]> triggerProperties = new List<TriggerProperty[]>();
                    List<int> outfitSlots = new List<int>();
                    foreach(int key in slotData.Keys)
                    {
                        triggerProperties.Add(slotData[key]);
                        outfitSlots.Add(key);
                    }
                    AmazingNewAccessoryLogic.Logger.LogDebug($"Found 2 clothing slots {outfitSlots} for acc {slot}:");
                    for (int i = 0; i < 4; i++) // for each state in outfitSlot1
                    {
                        TriggerProperty A = triggerProperties[0][i];
                        if (!validTrigger(A)) continue; // if A is a trigger that can never be true
                        TriggerProperty[] outfitSlot2 = triggerProperties[1];

                        List<int> statesThatTurnTheOutputOn = new List<int>();
                        foreach (TriggerProperty B in outfitSlot2)
                        {
                            if (
                                ( A.Visible && B.Visible) // both claim visible
                                || (A.Visible && (A.Priority >= B.Priority)) // A claims visible and has a higher or same priority as B
                                || (B.Visible && (A.Priority < B.Priority)) // B claims visible and has a higher priority as A
                            )
                            {
                                if (validTrigger(B)) statesThatTurnTheOutputOn.Add(B.ClothingState);
                            }
                        }
                        AmazingNewAccessoryLogic.Logger.LogDebug($"-> ClothingSlot A ({outfitSlots[0]}) State {i} claims visible={A.Visible} and priority={A.Priority}; B ({outfitSlots[1]}) claims visible={outfitSlot2.Select(trig => trig.Visible)} and priority={outfitSlot2.Select(trig => trig.Priority)}, therefor sates that turn the output on {statesThatTurnTheOutputOn} ");
                        switch (statesThatTurnTheOutputOn.Count)
                        {
                            case 0:
                                AmazingNewAccessoryLogic.Logger.LogDebug($"--> ClothingSlot A has full priority, and visible is {A.Visible}");
                                if (i == 3 && output is LogicFlowNode_OR)
                                {
                                    graph.getAllNodes().ForEach(node =>
                                    {
                                        if (node.inputAt(0) != null && node.inputAt(0).index == output.index)
                                        {
                                            node.SetInput(output.inputAt(1).index, 0);
                                        }
                                        else if (node.inputAt(1) != null && node.inputAt(1).index == output.index)
                                        {
                                            node.SetInput(output.inputAt(1).index, 1);
                                        }
                                    });
                                    graph.RemoveNode(output.index);
                                }
                                break;
                            case 1:
                            case 2:
                            case 3:
                                AmazingNewAccessoryLogic.Logger.LogDebug($"--> ClothingSlot B has priority on states {statesThatTurnTheOutputOn}");
                                LogicFlowNode connectedInputs = connectInputs(statesThatTurnTheOutputOn, outfitSlots[1], outfit, graph);
                                LogicFlowNode_AND and = addAndGateForInputs(connectedInputs.index, getInput(A.ClothingSlot, A.ClothingState, outfit).index, outfit);
                                and.setPosition(connectedInputs.getPosition() + new Vector2(75, 0));
                                if (i != 3)
                                {
                                    LogicFlowNode_OR or = addGate(outfit, 2) as LogicFlowNode_OR;
                                    or.setPosition(and.getPosition() + new Vector2(75, 0));
                                    or.SetInput(and.index, 1);
                                    output.SetInput(or.index, 0);
                                    output = or;
                                }
                                else
                                {
                                    output.SetInput(and.index, 0);
                                }
                                break;
                            case 4:
                                AmazingNewAccessoryLogic.Logger.LogDebug($"--> ClothingSlot A has full priority, and visible is {A.Visible}");
                                if (i != 3)
                                {
                                    LogicFlowNode input = getInput(A.ClothingSlot, A.ClothingState, outfit);
                                    LogicFlowNode_OR or = addGate(outfit, 2) as LogicFlowNode_OR;
                                    or.setPosition(input.getPosition() + new Vector2(75, 0));
                                    or.SetInput(input.index, 1);
                                    output.SetInput(or.index, 0);
                                    output = or;
                                }
                                else
                                {
                                    output.SetInput(getInput(A.ClothingSlot, A.ClothingState, outfit).index, 0);
                                }
                                break;
                        }

                    }
                }

                if (slotData.Keys.Count > 2)
                {
                    AmazingNewAccessoryLogic.Logger.LogWarning($"Found {slotData.Keys.Count} clothing slots ({slotData.Keys.ToList()}) connected to slot {slot}. Due to the complexity of the translation a max or 2 clothing slots is supported.");
                }

                doneSlots.Add(slot);
            }
        }


        public void TranslateFromAssForCharacter(int? OutfitSlot = null, ChaFile chaFile = null)
        {
            if (chaFile == null) chaFile = MakerAPI.LastLoadedChaFile ?? ChaFileControl;
            PluginData _pluginData = ExtendedSave.GetExtendedDataById(chaFile, "madevil.kk.ass");
            if (_pluginData == null)
            {
                AmazingNewAccessoryLogic.Logger.LogInfo($"No ASS Data found on {chaFile.charaFileName}" + (OutfitSlot.HasValue ? $" and slot {OutfitSlot.Value}" : ""));
                return;
            }

            List<TriggerProperty> _triggers = new List<TriggerProperty>();
            if (_pluginData.data.TryGetValue("TriggerPropertyList", out var ByteData) && ByteData != null)
            {
                _triggers = MessagePackSerializer.Deserialize<List<TriggerProperty>>((byte[])ByteData);
#if KKS
                //AmazingNewAccessoryLogic.Logger.LogInfo(MakerAPI.LastLoadedChaFile.GetLastErrorCode());
                //AmazingNewAccessoryLogic.Logger.LogInfo(ChaFileControl.GetLastErrorCode());
                if (kkcompatibility)
                {
                    _triggers.ForEach(t => t.Coordinate = OutfitKK2KKS(t.Coordinate));
                }
#endif
                if (OutfitSlot.HasValue) _triggers = _triggers.Where(x => x.Coordinate == OutfitSlot.Value).ToList();
            }
            if (_triggers.IsNullOrEmpty())
            {
                AmazingNewAccessoryLogic.Logger.LogInfo($"No Valid TriggerProperties found on {chaFile.charaFileName}" + (OutfitSlot.HasValue ? $" and slot {OutfitSlot.Value}":""));
                return;
            }
            AmazingNewAccessoryLogic.Logger.LogInfo($"Found {_triggers.Count} valid TriggerProperties on {chaFile.charaFileName}" + (OutfitSlot.HasValue ? $" and slot {OutfitSlot.Value}" : ""));
            // <Outfit, <AccessorySlot, <ClothingSlot, <ClothingState>>>>
            Dictionary<int, Dictionary<int, Dictionary<int, TriggerProperty[]>>> triggersForSlotForOutfit = new Dictionary<int, Dictionary<int, Dictionary<int, TriggerProperty[]>>>();
            foreach (TriggerProperty tp in _triggers)
            {
                if (tp.ClothingSlot >= 9) // There is only clothing slots 0-8, so >=9 indicates a custom group
                {
                    AmazingNewAccessoryLogic.Logger.LogInfo($"Coustom group trigger property found for Accessory {tp.Slot}, ClothingSlot {tp.ClothingSlot}; This is not supported (yet)!");
                    continue;
                }
                if (!triggersForSlotForOutfit.ContainsKey(tp.Coordinate)) triggersForSlotForOutfit.Add(tp.Coordinate, new Dictionary<int, Dictionary<int, TriggerProperty[]>>());
                if (!triggersForSlotForOutfit[tp.Coordinate].ContainsKey(tp.Slot)) triggersForSlotForOutfit[tp.Coordinate].Add(tp.Slot, new Dictionary<int, TriggerProperty[]>());
                if (!triggersForSlotForOutfit[tp.Coordinate][tp.Slot].ContainsKey(tp.ClothingSlot)) triggersForSlotForOutfit[tp.Coordinate][tp.Slot].Add(tp.ClothingSlot, new TriggerProperty[4]);
                triggersForSlotForOutfit[tp.Coordinate][tp.Slot][tp.ClothingSlot][tp.ClothingState] = tp;
            }

            foreach(int key in triggersForSlotForOutfit.Keys)
            {
                CreateNodesForAssData(triggersForSlotForOutfit[key], key);
            }
        }

        public void TranslateFromAssForCoordinate(ChaFileCoordinate coordinate = null)
        {
            if (coordinate != null)
            {
                coordinate = ChaControl.nowCoordinate;
            }
            PluginData _pluginData = ExtendedSave.GetExtendedDataById(coordinate, "madevil.kk.ass");
            if (_pluginData == null)
            {
                AmazingNewAccessoryLogic.Logger.LogInfo($"No ASS Data found on {coordinate.coordinateName}");
                return;
            }

            AmazingNewAccessoryLogic.Logger.LogInfo("Reading ASS Data");
            List<TriggerProperty> _triggers = new List<TriggerProperty>();
            if (_pluginData.data.TryGetValue("TriggerPropertyList", out var ByteData) && ByteData != null)
            {
                _triggers = MessagePackSerializer.Deserialize<List<TriggerProperty>>((byte[])ByteData);
            }
            if (_triggers.IsNullOrEmpty())
            {
                AmazingNewAccessoryLogic.Logger.LogInfo($"No TriggerProperties found on {coordinate.coordinateName}");
                return;
            }
            AmazingNewAccessoryLogic.Logger.LogInfo($"Found {_triggers.Count} valid TriggerProperties on {coordinate.coordinateName}");
            AmazingNewAccessoryLogic.Logger.LogInfo($"Processing ASS Data: {_triggers.Count} TriggerProperties found");
            // <AccessorySlot, <ClothingSlot, <ClothingState>>>
            Dictionary<int, Dictionary<int, TriggerProperty[]>> triggersForSlot = new Dictionary<int, Dictionary<int, TriggerProperty[]>>();
            foreach(TriggerProperty tp in _triggers)
            {
                if (tp.ClothingSlot >= 9) // There is only clothing slots 0-8, so >=9 indicates a custom group
                {
                    AmazingNewAccessoryLogic.Logger.LogInfo($"Coustom group trigger property found for Accessory {tp.Slot}, ClothingSlot {tp.ClothingSlot}; This is not supported (yet)!");
                    continue;
                }
                if (!triggersForSlot.ContainsKey(tp.Slot)) triggersForSlot.Add(tp.Slot, new Dictionary<int, TriggerProperty[]>());
                if (!triggersForSlot[tp.Slot].ContainsKey(tp.ClothingSlot)) triggersForSlot[tp.Slot].Add(tp.ClothingSlot, new TriggerProperty[4]);
                triggersForSlot[tp.Slot][tp.ClothingSlot][tp.ClothingState] = tp;
            }

            CreateNodesForAssData(triggersForSlot, ChaControl.fileStatus.coordinateType);
        }
        #endregion
    }

    public enum InputKey
    {
        TopOn = 1001,
        TopShift = 1002,
        BottomOn = 1003,
        BottomShift = 1004,
        BraOn = 1005,
        BraShift = 1006,
        UnderwearOn = 1007,
        UnderwearShift = 1008,
        UnderwearHang = 1009,
        GlovesOn = 1010,
        GlovesShift = 1011,
        GlovesHang = 1012,
        PantyhoseOn = 1013,
        PantyhoseShift = 1014,
        PantyhoseHang = 1015,
        LegwearOn = 1016,
#if KKS
        ShoesOn = 1018,
#else
        ShoesIndoorOn = 1017,
        ShoesOutdoorOn = 1018
#endif
    }

    public enum AdvancedInputType
    {
        Trigger, // not implemented (yet)
        Accessory,
        HandPtn,
        EyesPtn,
        EyesOpn,
        MouthPtn,
        MouthOpn,
        EyebrowPtn
    }
}
