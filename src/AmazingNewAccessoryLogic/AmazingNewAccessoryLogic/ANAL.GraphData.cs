using LogicFlows;
using System;
using System.Collections.Generic;
using System.Text;

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

        public GraphData(LogicFlowGraph logicGraph, bool isAdvanced) {
            graph = logicGraph;
            _advanced = isAdvanced;
        }
    }
}
