using LogicFlows;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public Dictionary<int, BindingType?> bindings = new Dictionary<int, BindingType?>();
        public Dictionary<int, byte> activeBoundStates = new Dictionary<int, byte>();

        public GraphData(LogicFlowGraph logicGraph, bool isAdvanced) {
            graph = logicGraph;
            _advanced = isAdvanced;
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
