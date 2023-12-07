using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using KKAPI;
using KKAPI.Chara;
using ExtensibleSaveFormat;
using MessagePack;
using LogicFlows;
using UnityEngine;
using KKAPI.Maker;

namespace AmazingNewAccessoryLogic
{
    class AnalCharaController : CharaCustomFunctionController
    {
        public LogicFlowGraph lfg { get => getCurrentGraph(); private set => setCurrentGraph(value); }

        private Dictionary<int, LogicFlowGraph> graphs = new Dictionary<int, LogicFlowGraph>();
        private Dictionary<int, List<int>> activeSlots = new Dictionary<int, List<int>>();

        private bool displayGraph = false;
        private static Material mat = new Material(Shader.Find("Hidden/Internal-Colored"));

        //render bodge 
        private RenderTexture rTex;
        private Camera rCam;

        private byte[] oldClothStates;

        private string slotInputText = "1";

        private Dictionary<int, Dictionary<int, Dictionary<int, LogicFlowNode_OR>>> placedOrs = new Dictionary<int, Dictionary<int, Dictionary<int, LogicFlowNode_OR>>>();
        private Dictionary<int, Dictionary<int, Dictionary<int, LogicFlowNode_AND>>> placedAnds = new Dictionary<int, Dictionary<int, Dictionary<int, LogicFlowNode_AND>>>();
        private Dictionary<int, Dictionary<int, LogicFlowNode_NOT>> placedNots = new Dictionary<int, Dictionary<int, LogicFlowNode_NOT>>();

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
            placedOrs.Clear();
            placedNots.Clear();
            placedAnds.Clear();

            PluginData data = GetExtendedData();
            if (data == null)
            {
                TranslateFromAssForCharacter();
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
            placedOrs.Remove(ChaControl.fileStatus.coordinateType);
            placedAnds.Remove(ChaControl.fileStatus.coordinateType);
            placedNots.Remove(ChaControl.fileStatus.coordinateType);

            PluginData data = GetCoordinateExtendedData(coordinate);
            if (data == null)
            {
                TranslateFromAssForCoordinate(coordinate);
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
                destNode.setInput(0,sourceNode.inputAt(0).index);
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
                if (dOutput == null) deserialiseNode(destinationOutfit, SerialisedNode.Serialise(graphs[sourceOutift].getNodeAt(1000000 + slot)));
                foreach(int index in iTree)
                {
                    LogicFlowNode node = graphs[destinationOutfit].getNodeAt(index);
                    if (node == null) deserialiseNode(destinationOutfit, SerialisedNode.Serialise(graphs[sourceOutift].getNodeAt(index)));
                }
            }
            graphs[destinationOutfit].isLoading = false;
        }

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
                    node.setInput(0, sNode.data[0]);
                    break;
                case SerialisedNode.NodeType.Gate_AND:
                    node = new LogicFlowNode_AND(graphs[outfit], key: sNode.index) { label = "AND", toolTipText = "AND" };
                    node.setInput(0, sNode.data[0]);
                    node.setInput(1, sNode.data[1]);
                    break;
                case SerialisedNode.NodeType.Gate_OR:
                    node = new LogicFlowNode_OR(graphs[outfit], key: sNode.index) { label = "OR", toolTipText = "OR" };
                    node.setInput(0, sNode.data[0]);
                    node.setInput(1, sNode.data[1]);
                    break;
                case SerialisedNode.NodeType.Gate_XOR:
                    node = new LogicFlowNode_XOR(graphs[outfit], key: sNode.index) { label = "XOR", toolTipText = "XOR" };
                    node.setInput(0, sNode.data[0]);
                    node.setInput(1, sNode.data[1]);
                    break;
                case SerialisedNode.NodeType.Input:
                    node = addInput((InputKey)sNode.index, sNode.postion, outfit);
                    break;
                case SerialisedNode.NodeType.Output:
                    node = addOutput(sNode.data[0], outfit);
                    node.setInput(0, sNode.data[1]);
                    break;
            }
            if (node != null)
            {
                node.enabled = sNode.enabled;
                node.setPosition(sNode.postion);
            }
        }

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

        private LogicFlowGraph createGraph(int? outfit = null)
        {
            if (!outfit.HasValue) outfit = ChaControl.fileStatus.coordinateType;
            if (graphs == null) graphs = new Dictionary<int, LogicFlowGraph>();
            graphs[outfit.Value] = new LogicFlowGraph(new Rect(200, 200, 500, 900));
            if (activeSlots == null) activeSlots = new Dictionary<int, List<int>>();
            activeSlots[outfit.Value] = new List<int>();
            float topY = graphs[outfit.Value].rect.height;

            int i = 1;
            foreach(InputKey key in Enum.GetValues(typeof(InputKey)))
            {
                addInput(key, new Vector2(10, topY - 50 * i), outfit.Value);
                i++;
            }
            // clear metainputs for graph
            createdMetaInputs.Remove(outfit.Value);
            return graphs[outfit.Value];
        }

        public LogicFlowInput addInput(InputKey key, Vector2 pos, int outift = -1)
        {
            LogicFlowGraph g;
            if (outift == -1) g = lfg;
            else g = graphs[outift];
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

        private Dictionary<int, Dictionary<int, LogicFlowNode_NOT>> createdMetaInputs = new Dictionary<int, Dictionary<int, LogicFlowNode_NOT>>();

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
            
            if (createdMetaInputs.ContainsKey(outfit.Value) && createdMetaInputs[outfit.Value].ContainsKey(clothingSlot)) return createdMetaInputs[outfit.Value][clothingSlot];

            if (!createdMetaInputs.ContainsKey(outfit.Value)) createdMetaInputs.Add(outfit.Value, new Dictionary<int, LogicFlowNode_NOT>());

            switch(clothingSlot)
            {
                case 0:
                case 1:
                case 2:
                    LogicFlowGate or = addOrGateForInputs((int)GetInputKey(clothingSlot, 0), (int)GetInputKey(clothingSlot, 1), outfit.Value);
                    or.setPosition(graph.getNodeAt((int)GetInputKey(clothingSlot, 0).Value).rect.position + new Vector2(75, 0));

                    LogicFlowNode_NOT not = addNotForInput(or.index, outfit.Value);
                    not.setPosition(or.rect.position + new Vector2(75, 0));
                    createdMetaInputs[outfit.Value].Add(clothingSlot, not);
                    return not;
                case 3:
                case 4:
                case 5:
                    LogicFlowGate or1 = addOrGateForInputs((int)GetInputKey(clothingSlot, 0), (int)GetInputKey(clothingSlot, 1), outfit.Value);
                    or1.setPosition(graph.getNodeAt((int)GetInputKey(clothingSlot, 0).Value).rect.position + new Vector2(75, 0));

                    LogicFlowGate or2 = addOrGateForInputs(or1.index, (int)GetInputKey(clothingSlot, 1), outfit.Value);
                    or2.setPosition(or1.rect.position + new Vector2(75, 0));

                    not = addNotForInput(or2.index, outfit.Value);
                    not.setPosition(or2.rect.position + new Vector2(75, 0));
                    createdMetaInputs[outfit.Value].Add(clothingSlot, not);
                    return not;
                case 6:
                case 7:
                case 8:
                    not = addNotForInput((int)GetInputKey(clothingSlot, 0).Value, outfit.Value);
                    not.setPosition(graph.getNodeAt((int)GetInputKey(clothingSlot, 0).Value).rect.position + new Vector2(75, 0));

                    createdMetaInputs[outfit.Value].Add(clothingSlot, not);
                    return not;
                default: return null;
            }
        }


        private LogicFlowNode_OR addOrGateForInputs(int inId1, int inId2, int outfit)
        {
            if (!placedOrs.ContainsKey(outfit)) placedOrs.Add(outfit, new Dictionary<int, Dictionary<int, LogicFlowNode_OR>>());

            int smaller = inId1 < inId2 ? inId1 : inId2;
            int bigger = inId1 >= inId2 ? inId1 : inId2;

            if (placedOrs[outfit].ContainsKey(smaller) && placedOrs[outfit][smaller].ContainsKey(bigger))
            {
                return placedOrs[outfit][smaller][bigger];
            }
            else
            {
                LogicFlowNode_OR or = addGate(outfit, 2) as LogicFlowNode_OR;
                or.setInput(0, smaller);
                or.setInput(1, bigger);
                if (!placedOrs[outfit].ContainsKey(smaller)) placedOrs[outfit].Add(smaller, new Dictionary<int, LogicFlowNode_OR>());
                placedOrs[outfit][smaller].Add(bigger, or);
                return or;
            }
        }


        private LogicFlowNode_AND addAndGateForInputs(int inId1, int inId2, int outfit)
        {
            if (!placedAnds.ContainsKey(outfit)) placedAnds.Add(outfit, new Dictionary<int, Dictionary<int, LogicFlowNode_AND>>());

            int smaller = inId1 < inId2 ? inId1 : inId2;
            int bigger = inId1 >= inId2 ? inId1 : inId2;

            if (placedAnds[outfit].ContainsKey(smaller) && placedAnds[outfit][smaller].ContainsKey(bigger))
            {
                return placedAnds[outfit][smaller][bigger];
            }
            else
            {
                LogicFlowNode_AND and = addGate(outfit, 1) as LogicFlowNode_AND;
                and.setInput(0, smaller);
                and.setInput(1, bigger);
                if (!placedAnds[outfit].ContainsKey(smaller)) placedAnds[outfit].Add(smaller, new Dictionary<int, LogicFlowNode_AND>());
                placedAnds[outfit][smaller].Add(bigger, and);
                return and;
            }
        }


        private LogicFlowNode_NOT addNotForInput(int inId, int outfit)
        {
            if (!placedNots.ContainsKey(outfit)) placedNots.Add(outfit, new Dictionary<int, LogicFlowNode_NOT>());
            if (placedNots[outfit].ContainsKey(inId)) return placedNots[outfit][inId];
            else
            {
                LogicFlowNode_NOT not = addGate(outfit, 0) as LogicFlowNode_NOT;
                not.setInput(0, inId);
                placedNots[outfit].Add(inId, not);
                return not;
            }
        }


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
                activeSlots[outfit.Value].Add(slot);
                LogicFlowOutput output = new LogicFlowOutput_Action((value) => setAccessoryState(slot, value), graphs[outfit.Value], key: 1000000 + slot) { label = $"Slot {slot + 1}", toolTipText = null };
                output.setPosition(new Vector2(
                    graphs[outfit.Value].rect.width - 80,
                    graphs[outfit.Value].rect.height - 50 * (activeSlots[outfit.Value].Count))
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
        /// <param name="type">0 = NOT, 1 = AND, 2 = OR, 3 = XOR</param>
        public LogicFlowGate addGate(byte type)
        {
            return addGate(ChaControl.fileStatus.coordinateType, type);
        }

        /// <summary>
        /// Add a logic gate node to the specifed graph
        /// </summary>
        /// <param name="outfit">Outfit Slot</param>
        /// <param name="type">0 = NOT, 1 = AND, 2 = OR, 3 = XOR</param>
        public LogicFlowGate addGate(int outfit, byte type)
        {
            if (graphs.ContainsKey(outfit))
            {
                LogicFlowGate gate;
                switch (type)
                {
                    case 0:
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
                    default:
                        gate = new LogicFlowNode_NOT(graphs[outfit]) { label = "NOT", toolTipText = "NOT" };
                        break;
                }
                gate.setPosition(graphs[outfit].rect.size/2 - (gate.rect.size / 2));
                return gate;
            }
            return null;
        }


        public bool getClothState(int clothType, byte stateValue)
        {
            return ChaControl.fileStatus.clothesState[clothType] == stateValue;
        }

        public void setAccessoryState(int accessorySlot, bool stateValue)
        {
            ChaControl.fileStatus.showAccessory[accessorySlot] = stateValue;
        }

        public void show()
        {
            if (lfg == null) createGraph();
            displayGraph = true;
            AnalCameraComponent acc = rCam.GetOrAddComponent<AnalCameraComponent>();
            acc.OnPostRenderEvent += postRenderEvent;

            GameCursor c = GameCursor.Instance;
            if (c != null)
            {
                c.SetCursorLock(false);
            }
        }

        public void hide()
        {
            displayGraph = false;
            if (MakerAPI.InsideAndLoaded) AmazingNewAccessoryLogic.Instance.toggle.Value = false;
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
                if (MakerAPI.InsideAndLoaded) AmazingNewAccessoryLogic.Instance.toggle.Value = false;
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

        void OnGUI()
        {
            if (lfg == null) return;
            if (displayGraph)
            {
                GUIStyle headerTextStyle = new GUIStyle(GUI.skin.label);
                headerTextStyle.normal.textColor = Color.black;
                headerTextStyle.alignment = TextAnchor.MiddleLeft;

                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), rTex);

                GUI.Label(new Rect(screenToGUI(lfg.rect.position + new Vector2(10, lfg.rect.height + 35)), new Vector2(250, 25)), $"AmazingNewAccessoryLogic v{AmazingNewAccessoryLogic.Version}", headerTextStyle);
                if (GUI.Button(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(-65, 32)), new Vector2(60, 20)), "Close"))
                {
                    this.hide();
                }

                GUI.Box(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(5, 0)), new Vector2(130, 210)), "");

                // add nodes buttons
                if (GUI.Button(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(10, -5)), new Vector2(120, 30)), "Add NOT Gate"))
                {
                    addGate(0);
                }
                if (GUI.Button(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(10, -40)), new Vector2(120, 30)), "Add AND Gate"))
                {
                    addGate(1);
                }
                if (GUI.Button(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(10, -75)), new Vector2(120, 30)), "Add OR Gate"))
                {
                    addGate(2);
                }
                if (GUI.Button(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(10, -110)), new Vector2(120, 30)), "Add XOR Gate"))
                {
                    addGate(3);
                }
                GUI.Label(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(15, -150)), new Vector2(80, 20)), "Acc-Slot:");
                slotInputText = GUI.TextField(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(90, -150)), new Vector2(40, 20)), slotInputText);
                if (GUI.Button(new Rect(screenToGUI(lfg.rect.position + lfg.rect.size + new Vector2(10, -175)), new Vector2(120, 30)), "Add Output"))
                {
                    
                    try
                    {
                        AmazingNewAccessoryLogic.Logger.LogInfo(slotInputText);
                        int slotInput = int.Parse(slotInputText);
                        if (!(slotInput - 1 >= 0)) 
                            AmazingNewAccessoryLogic.Logger.LogMessage("Slot not found. Please enter a slot index with 1 or higher!");
                        else if (!(slotInput - 1 < ChaControl.fileStatus.showAccessory.Length)) 
                            AmazingNewAccessoryLogic.Logger.LogMessage($"Slot not found. Please enter a slot index with {ChaControl.fileStatus.showAccessory.Length} or lower!");
                        else addOutput(slotInput - 1);
                    }
                    catch(Exception)
                    {
                        AmazingNewAccessoryLogic.Logger.LogMessage($"Could not parse slot. Please enter a valid integer number between 1 and {ChaControl.fileStatus.showAccessory.Length}!");
                    }
                    
                }

                lfg.ongui();
            }
        }

        private Vector2 screenToGUI(Vector2 screenPos)
        {
            return new Vector2(screenPos.x, Screen.height - screenPos.y);
        }

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
                Dictionary<int, TriggerProperty[]> slotData = data[slot];

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
                    switch (statesThatTurnTheOutputOn.Count)
                    {
                        case 0:
                            break;
                        case 1:
                        case 2:
                        case 3:
                            output.setInput(0, connectInputs(statesThatTurnTheOutputOn, clothingSlot, outfit, graph).index);
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

                    for (int i = 0; i < 4; i++) // for each state in outfitSlot1
                    {
                        TriggerProperty A = triggerProperties[0][i];
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

                        switch (statesThatTurnTheOutputOn.Count)
                        {
                            case 0:
                                break;
                            case 1:
                            case 2:
                            case 3:
                                LogicFlowNode connectedInputs = connectInputs(statesThatTurnTheOutputOn, outfitSlots[1], outfit, graph);
                                LogicFlowNode_AND and = addAndGateForInputs(connectedInputs.index, getInput(A.ClothingSlot, A.ClothingState, outfit).index, outfit);
                                and.setPosition(connectedInputs.rect.position + new Vector2(75, 0));
                                if (i != 3)
                                {
                                    LogicFlowNode_OR or = addGate(outfit, 2) as LogicFlowNode_OR;
                                    or.setPosition(and.rect.position + new Vector2(75, 0));
                                    or.setInput(1, and.index);
                                    output.setInput(0, or.index);
                                    output = or;
                                }
                                else
                                {
                                    output.setInput(0, and.index);
                                }
                                break;
                            case 4:
                                if (i != 3)
                                {
                                    LogicFlowNode input = getInput(A.ClothingSlot, A.ClothingState, outfit);
                                    LogicFlowNode_OR or = addGate(outfit, 2) as LogicFlowNode_OR;
                                    or.setPosition(input.rect.position + new Vector2(75, 0));
                                    or.setInput(1, input.index);
                                    output.setInput(0, or.index);
                                    output = or;
                                }
                                else
                                {
                                    output.setInput(0, getInput(A.ClothingSlot, A.ClothingState, outfit).index);
                                }
                                break;
                        }

                    }
                }


                doneSlots.Add(slot);
            }
        }

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
                    or0.setPosition(input1.rect.position + new Vector2(75, 0));
                    return or0;
                case 3:
                    LogicFlowNode input_1 = getInput(clothingSlot, statesThatTurnTheOutputOn[0], outfit);
                    LogicFlowNode input_2 = getInput(clothingSlot, statesThatTurnTheOutputOn[1], outfit);
                    LogicFlowNode input_3 = getInput(clothingSlot, statesThatTurnTheOutputOn[2], outfit);

                    LogicFlowNode_OR or1 = addOrGateForInputs(input_1.index, input_2.index, outfit);
                    LogicFlowNode_OR or2 = addOrGateForInputs(or1.index, input_3.index, outfit);

                    or1.setPosition(input_1.rect.position + new Vector2(75, 0));

                    or2.setPosition(or1.rect.position + new Vector2(75, 0));
                    return or2;
                case 4:
                    return null;
                default:
                    return null;
            }
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
                default: return -1;
            }
        }
#endif

        public void TranslateFromAssForCharacter(ChaFile chaFile = null)
        {
            if (chaFile == null) chaFile = MakerAPI.LastLoadedChaFile ?? ChaFileControl;
            PluginData _pluginData = ExtendedSave.GetExtendedDataById(chaFile, "madevil.kk.ass");
            if (_pluginData == null) return;
            List<TriggerProperty> _triggers = new List<TriggerProperty>();
            if (_pluginData.data.TryGetValue("TriggerPropertyList", out var ByteData) && ByteData != null)
            {
                _triggers = MessagePackSerializer.Deserialize<List<TriggerProperty>>((byte[])ByteData);
            }
            if (_triggers.IsNullOrEmpty()) return;
            // <Outfit, <AccessorySlot, <ClothingSlot, <ClothingState>>>>
            Dictionary<int, Dictionary<int, Dictionary<int, TriggerProperty[]>>> triggersForSlotForOutfit = new Dictionary<int, Dictionary<int, Dictionary<int, TriggerProperty[]>>>();
            foreach (TriggerProperty tp in _triggers)
            {
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
            if (_pluginData == null) return;
            AmazingNewAccessoryLogic.Logger.LogInfo("Reading ASS Data");
            List<TriggerProperty> _triggers = new List<TriggerProperty>();
            if (_pluginData.data.TryGetValue("TriggerPropertyList", out var ByteData) && ByteData != null)
            {
                _triggers = MessagePackSerializer.Deserialize<List<TriggerProperty>>((byte[])ByteData);
#if KKS
                if (ChaFileControl.GetLastErrorCode() == -1)
                {
                    _triggers.ForEach(t => t.Coordinate = OutfitKK2KKS(t.Coordinate));
                }
#endif
            }
            if (_triggers.IsNullOrEmpty()) return;

            AmazingNewAccessoryLogic.Logger.LogInfo($"Processing ASS Data: {_triggers.Count} TriggerProperties found");
            // <AccessorySlot, <ClothingSlot, <ClothingState>>>
            Dictionary<int, Dictionary<int, TriggerProperty[]>> triggersForSlot = new Dictionary<int, Dictionary<int, TriggerProperty[]>>();
            foreach(TriggerProperty tp in _triggers)
            {
                if (!triggersForSlot.ContainsKey(tp.Slot)) triggersForSlot.Add(tp.Slot, new Dictionary<int, TriggerProperty[]>());
                if (!triggersForSlot[tp.Slot].ContainsKey(tp.ClothingSlot)) triggersForSlot[tp.Slot].Add(tp.ClothingSlot, new TriggerProperty[4]);
                triggersForSlot[tp.Slot][tp.ClothingSlot][tp.ClothingState] = tp;
            }

            CreateNodesForAssData(triggersForSlot, ChaControl.fileStatus.coordinateType);
        }

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
}
