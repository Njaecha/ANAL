using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using LogicFlows;
using UnityEngine;
using BepInEx.Logging;
using KKAPI;
using KKAPI.Maker;
using KKAPI.Maker.UI.Sidebar;
using UniRx;
using KKAPI.Chara;
using IllusionFixes;

namespace AmazingNewAccessoryLogic
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    //[BepInDependency(MakerOptimizations.GUID)] would prevent use in Studio
    class AmazingNewAccessoryLogic : BaseUnityPlugin
    {
        public const string PluginName = "AmazingNewAccessoryLogic";
        public const string GUID = "org.njaecha.plugins.anal";
        public const string Version = "0.0.3";

        internal new static ManualLogSource Logger;

        internal SidebarToggle toggle;

        public static AmazingNewAccessoryLogic Instance;

        void Awake()
        {
            MakerAPI.MakerBaseLoaded += createSideBarToggle;
            Logger = base.Logger;


            CharacterApi.RegisterExtraBehaviour<AnalCharaController>(GUID);
            Instance = this;

            AccessoriesApi.AccessoryTransferred += AccessoryTransferred;
            AccessoriesApi.AccessoriesCopied += AccessoriesCopied;
        }

        private void AccessoriesCopied(object sender, AccessoryCopyEventArgs e)
        {
            ChaFileDefine.CoordinateType dType = e.CopyDestination;
            ChaFileDefine.CoordinateType sType = e.CopySource;
            IEnumerable<int> slots = e.CopiedSlotIndexes;
            MakerAPI.GetCharacterControl().gameObject.GetComponent<AnalCharaController>().AccessoriesCopied((int)sType, (int)dType, slots);

        }

        private void AccessoryTransferred(object sender, AccessoryTransferEventArgs e)
        {
            int dSlot = e.DestinationSlotIndex;
            int sSlot = e.SourceSlotIndex;
            MakerAPI.GetCharacterControl().gameObject.GetComponent<AnalCharaController>().AccessoryTransferred(sSlot, dSlot);

        }

        private void showGraphInMaker(bool b)
        {
            if (b) MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>()?.show();
            else MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>()?.hide();
        }

        private void createSideBarToggle(object sender, RegisterCustomControlsEvent e)
        {
            toggle = e.AddSidebarControl(new SidebarToggle("Show ANAL", false, this));
            toggle.ValueChanged.Subscribe(delegate (bool b) {
                showGraphInMaker(b);
            });
        }

        internal CursorManager getMakerCursorMangaer()
        {
            return base.gameObject.GetComponent<CursorManager>();
        }
    }
}
