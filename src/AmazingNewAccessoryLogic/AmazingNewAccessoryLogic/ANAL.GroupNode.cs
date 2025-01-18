using UnityEngine;
using LogicFlows;
using System.Collections.Generic;

namespace AmazingNewAccessoryLogic {
    public class LogicFlowNode_GRP : LogicFlowGate {
        public static object requestor = null;

        // These dictionaries are dic<controlledThing, dic<state, valueForState>>
        int state = 0;
        Dictionary<int, Dictionary<int, bool>> controlledAccs = new Dictionary<int, Dictionary<int, bool>>();
        Dictionary<LogicFlowNode, Dictionary<int, bool>> controlledNodes = new Dictionary<LogicFlowNode, Dictionary<int, bool>>();

        protected override Rect initRect() {
            return new Rect(50f, 50f, 40f, 20f);
        }

        public LogicFlowNode_GRP(LogicFlowGraph parentGraph, int? key = null) : base(new int?[1], parentGraph, key) {
            toolTipText = "GRP";
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
                controlledAccs = new Dictionary<int, Dictionary<int, bool>>(controlledAccs),
                controlledNodes = new Dictionary<LogicFlowNode, Dictionary<int, bool>>(controlledNodes),
            };
            foreach (var key in controlledAccs.Keys) {
                clonedGroup.controlledAccs[key] = new Dictionary<int, bool>(controlledAccs[key]);
            }
            foreach (var key in controlledNodes.Keys) {
                clonedGroup.controlledNodes[key] = new Dictionary<int, bool>(controlledNodes[key]);
            }
            clonedGroup.setPositionUI(rect.position + new Vector2(20f, 20f));
        }

        public override bool getValue() {
            if (!enabled || requestor == null || !(inputAt(0)?.getValue() ?? true)) {
                return false;
            }

            bool result;
            switch (requestor) {
                case int acc:
                    result = controlledAccs.TryGetValue(acc, out var setAccs) && setAccs.TryGetValue(state, out var valAcc) && valAcc;
                    break;
                case LogicFlowNode node:
                    result = controlledNodes.TryGetValue(node, out var setNodes) && setNodes.TryGetValue(state, out var valNode) && valNode;
                    break;
                default:
                    result = false;
                    break;
            }

            requestor = null;
            return result;
        }
    }
}