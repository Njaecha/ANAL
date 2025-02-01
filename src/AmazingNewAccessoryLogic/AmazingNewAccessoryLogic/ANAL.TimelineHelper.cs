using Studio;
using System.Xml;
using System.Linq;
using KKAPI.Utilities;

namespace AmazingNewAccessoryLogic
{
    internal class TimelineHelper
    {
        internal static LogicFlowNode_GRP groupToAnimate;

        internal static void PopulateTimeline()
        {
            if (!TimelineCompatibility.IsTimelineAvailable()) return;

            // Group state
            TimelineCompatibility.AddInterpolableModelDynamic(
                owner: "ANAL",
                id: "groupState",
                name: "Group State",
                interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
                    GetGroup(oci, parameter).state = leftValue,
                interpolateAfter: null,
                getValue: (oci, parameter) => GetGroup(oci, parameter)?.state ?? 0,
                readValueFromXml: (parameter, node) => XmlConvert.ToInt16(node.Attributes["value"].Value),
                writeValueToXml: (parameter, writer, value) =>
                    writer.WriteAttributeString("value", XmlConvert.ToString(value)),
                readParameterFromXml: ReadParameter,
                writeParameterToXml: WriteParameter,
                getParameter: GetParameter,
                checkIntegrity: (oci, parameter, leftValue, rightValue) => GetGroup(oci, parameter) != null,
                getFinalName: (currentName, oci, parameter) =>
                    $"{GetGroup(oci, parameter)?.getName() ?? "Unknown"} Group State",
                isCompatibleWithTarget: (oci) =>
                    oci is OCIChar ociChar && ociChar.charInfo == groupToAnimate?.ctrl.ChaControl
            );
        }

        private static LogicFlowNode_GRP GetGroup(ObjectCtrlInfo oci, GroupParam parameter)
        {
            if (oci == null || parameter == null || !(oci is OCIChar ociChar)) return null;
            var ctrl = ociChar.charInfo.GetComponent<AnalCharaController>();
            if (ctrl == null || !ctrl.graphs.TryGetValue(parameter.outfit, out var lfg)) return null;
            var node = lfg.nodes.Values.FirstOrDefault(n => n.index == parameter.index);
            return node is LogicFlowNode_GRP grp ? grp : null;
        }

        private static GroupParam GetParameter(ObjectCtrlInfo oci)
        {
            int outfit = groupToAnimate.ctrl.graphs.First(x => x.Value == groupToAnimate.parentGraph).Key;
            return new GroupParam(outfit, groupToAnimate.index);
        }

        private static void WriteParameter(ObjectCtrlInfo oci, XmlTextWriter writer, GroupParam parameter)
        {
            writer.WriteAttributeString("Outfit", parameter.outfit.ToString());
            writer.WriteAttributeString("Index", parameter.index.ToString());
        }

        private static GroupParam ReadParameter(ObjectCtrlInfo oci, XmlNode node)
        {
            if (!int.TryParse(node.Attributes["Outfit"].Value, out int outfit)) return GroupParam.none;
            if (!int.TryParse(node.Attributes["Index"].Value, out int index)) return GroupParam.none;
            return new GroupParam(outfit, index);
        }

        internal static void SelectGroup(LogicFlowNode_GRP group)
        {
            groupToAnimate = group;
            TimelineCompatibility.RefreshInterpolablesList();
        }

        private class GroupParam
        {
            private static GroupParam _none;

            public static GroupParam none
            {
                get
                {
                    if (_none == null)
                    {
                        _none = new GroupParam(0, 0);
                    }

                    return _none;
                }
            }

            public int outfit = 0;
            public int index = 0;

            public GroupParam(int outfit, int index)
            {
                this.outfit = outfit;
                this.index = index;
            }
        }
    }
}