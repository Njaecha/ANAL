﻿using HarmonyLib;
using LogicFlows;
using System.Collections;
using UnityEngine;

namespace AmazingNewAccessoryLogic
{
    public static class Hooks
    {
        private static Harmony _harmony;

        // Setup Harmony and patch methods
        internal static void SetupHooks()
        {
            _harmony = Harmony.CreateAndPatchAll(typeof(Hooks), null);
        }

        // Disable Harmony patches of this plugin
        internal static void UnregisterHooks()
        {
            _harmony.UnpatchSelf();
        }

        // Set requestor field of the GRP node class so getValue knows what is requesting the current value
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LogicFlowNode), nameof(LogicFlowNode.inputAt))]
        private static void LogicFlowNodeAfterInputAt(LogicFlowNode __result, LogicFlowNode __instance)
        {
            if (__result != null && __result is LogicFlowNode_GRP) LogicFlowNode_GRP.requestor = __instance;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LogicFlowNode), nameof(LogicFlowNode.drawLabel))]
        private static bool LogicFlowNodeBeforeDrawLabel(LogicFlowNode __instance)
        {
            if (__instance is LogicFlowNode_GRP grp)
            {
                if (!grp.label.IsNullOrEmpty())
                {
                    GUIStyle guistyle = new GUIStyle(GUI.skin.box)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = (int)(14f * grp.parentGraph.getUIScale())
                    };
                    guistyle.normal.textColor = Color.black;
                    guistyle.padding.top =
                        (guistyle.padding.bottom = (guistyle.padding.left = (guistyle.padding.right = 0)));
                    guistyle.normal.background = LogicFlowBox.GetBackground();
                    float labelWidth = guistyle.CalcSize(new GUIContent(grp.label)).x + 5f;
                    float left = grp.parentGraph.A.x + (grp.C.x + grp.B.x - labelWidth) / 2;
                    float top = Screen.height - (grp.parentGraph.A.y + grp.B.y) - grp.parentGraph.getUIScale() * 25f;
                    float width = Mathf.Max(grp.C.x - grp.B.x, labelWidth);
                    float height = grp.parentGraph.getUIScale() * 20f;
                    GUI.Label(new Rect(left, top, width, height), grp.label, guistyle);
                    if (GUI.Button(new Rect(left - height - 3, top, height, height), "<", guistyle))
                    {
                        grp.state--;
                    }

                    if (GUI.Button(new Rect(left + width + 3, top, height, height), ">", guistyle))
                    {
                        grp.state++;
                    }
                }

                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeAccessory), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(bool))]
        private static void ChaControlAfterChangeAccessory(ChaControl __instance, int slotNo, int type)
        {
            if (KKAPI.Maker.MakerAPI.InsideMaker && type == 0)
            {
                __instance.GetComponent<AnalCharaController>().AccessoryKindChanged(slotNo, true);
            }
        }
        
        [HarmonyPrefix, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCoordinateType), typeof(ChaFileDefine.CoordinateType), typeof(bool))]
        private static void ChangeCoordinateTypePrefix(ChaControl __instance)
        {
            AnalCharaController c = __instance.GetComponent<AnalCharaController>();
            if (c)
            {
                c.OutfitChanged();
            }
        }
    }
}