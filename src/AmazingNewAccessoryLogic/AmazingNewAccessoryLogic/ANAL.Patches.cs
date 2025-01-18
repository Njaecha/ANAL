﻿using HarmonyLib;
using LogicFlows;

namespace AmazingNewAccessoryLogic {
    public static class Patches {
        internal static void Patch() {
            Hooks.SetupHooks();
        }

        internal static void Unpatch() {
            Hooks.UnregisterHooks();
        }

        public static class Hooks {
            private static Harmony _harmony;

            // Setup Harmony and patch methods
            internal static void SetupHooks() {
                _harmony = Harmony.CreateAndPatchAll(typeof(Hooks), null);
            }

            // Disable Harmony patches of this plugin
            internal static void UnregisterHooks() {
                _harmony.UnpatchSelf();
            }

            // Set requestor field of the GRP node class so getValue knows what is requesting the current value
            [HarmonyPostfix]
            [HarmonyPatch(typeof(LogicFlowNode), "inputAt")]
            private static void LogicFlowNodeAfterInputAt(LogicFlowNode __result, LogicFlowNode __instance) {
                if (__result != null && __result is LogicFlowNode_GRP) LogicFlowNode_GRP.requestor = __instance;
            }
        }
    }
}
