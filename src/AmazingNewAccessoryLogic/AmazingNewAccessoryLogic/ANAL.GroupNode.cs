using LogicFlows;
using UnityEngine;
using System.Collections.Generic;

namespace AmazingNewAccessoryLogic {
    public class LogicFlowNode_GRP : LogicFlowGate {
        public static LogicFlowNode requestor = null;

        private int _state = 0;
        internal int state {
            get {
                return _state;
            }
            set {
                _state = value;
                if (!controlledNodes.ContainsKey(_state)) {
                    controlledNodes[_state] = new HashSet<LogicFlowNode>();
                }
            }
        }

        // dic<state, set<enabledThingForThisState>>
        private Dictionary<int, HashSet<LogicFlowNode>> controlledNodes = new Dictionary<int, HashSet<LogicFlowNode>>();

        protected override Rect initRect() {
            return new Rect(50f, 50f, 40f, 20f);
        }

        public LogicFlowNode_GRP(LogicFlowGraph parentGraph, int? key = null, string name = null) : base(new int?[1], parentGraph, key) {
            setName(name ?? "GRP");
            calcTooltip();
        }

        private void calcTooltip() {
            int min = 0;
            int max = 0;
            foreach (var kvp in controlledNodes) {
                if (kvp.Value.Count > 0) {
                    if (kvp.Key < min) min = kvp.Key;
                    if (kvp.Key > max) max = kvp.Key;
                }
            }
            toolTipText = $"Group Node (Min: {min}, Max: {max})";
        }

        public void addActiveNode(LogicFlowNode node, int? __state = null) {
            int stateToAddTo = __state.GetValueOrDefault(state);
            if (!controlledNodes.ContainsKey(stateToAddTo)) controlledNodes[stateToAddTo] = new HashSet<LogicFlowNode>();
            controlledNodes[stateToAddTo].Add(node);
            calcTooltip();
        }

        public void removeActiveNode(LogicFlowNode node, int? __state = null) {
            int stateToRemoveFrom = __state.GetValueOrDefault(state);
            if (!controlledNodes.ContainsKey(stateToRemoveFrom)) return;
            controlledNodes[stateToRemoveFrom].Remove(node);
            calcTooltip();
        }

        public void setName(string newName) {
            label = $"{newName}: {state}";
        }

        public string getName() {
            return label.TrimEnd(new[] { '-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ':', ' ' }).Trim();
        }

        public override void drawSymbol() {
            float h = rect.height * 0.4f;
            float w = rect.width * 0.4f;
            Vector2 vector = A + new Vector2(rect.width * 0.5f, rect.height * 0.5f);
            GL.Begin(7);
            GL.Color(enabled ? (inputAt(0)?.getValue() ?? true ? trueColor : falseColor) : disabledColor);
            GL.Vertex(translateToGL(vector + new Vector2(w, 0)));
            GL.Vertex(translateToGL(vector + new Vector2(0, h)));
            GL.Vertex(translateToGL(vector + new Vector2(-w, 0)));
            GL.Vertex(translateToGL(vector + new Vector2(0, -h)));
            GL.End();
        }

        protected override void clone() {
            LogicFlowNode_GRP clonedGroup = new LogicFlowNode_GRP(parentGraph) {
                label = label,
                toolTipText = toolTipText,
                controlledNodes = new Dictionary<int, HashSet<LogicFlowNode>>(controlledNodes),
            };
            foreach (var key in controlledNodes.Keys) {
                clonedGroup.controlledNodes[key] = new HashSet<LogicFlowNode>(controlledNodes[key]);
            }
            clonedGroup.setPositionUI(rect.position + new Vector2(20f, 20f));
        }

        public override bool getValue() {
            if (!enabled || requestor == null || !(inputAt(0)?.getValue() ?? true)) {
                return false;
            }

            bool result = controlledNodes.TryGetValue(state, out var setNodes) && setNodes.Contains(requestor);
            requestor = null;
            return result;
        }
    }
}