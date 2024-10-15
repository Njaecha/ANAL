using System;
using UnityEngine;
using HarmonyLib;

namespace AmazingNewAccessoryLogic
{
	internal class ANALHooks
    {
        [HarmonyPrefix, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCoordinateType), typeof(ChaFileDefine.CoordinateType), typeof(bool))]
        private static void ChangeCoordinateTypePrefix(ChaControl __instance)
        {
            AnalCharaController c = __instance.GetComponent<AnalCharaController>();
            if (c != null)
            {
                c.lfg?.ForceUpdate();
            }
        }
    }
}

