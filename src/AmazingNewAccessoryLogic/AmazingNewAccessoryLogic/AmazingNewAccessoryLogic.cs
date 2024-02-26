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
using BepInEx.Configuration;
using KKAPI.Maker.UI;
using KKAPI.Studio.UI;
using KKAPI.Studio;
using HarmonyLib;

namespace AmazingNewAccessoryLogic
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    //[BepInDependency(MakerOptimizations.GUID)] would prevent use in Studio
    class AmazingNewAccessoryLogic : BaseUnityPlugin
    {
        public const string PluginName = "AmazingNewAccessoryLogic";
        public const string GUID = "org.njaecha.plugins.anal";
        public const string Version = "0.0.7";

        internal new static ManualLogSource Logger;

        public static SidebarToggle SidebarToggle;
        public static MakerButton AccessoryButton;
        public static MakerButton AccessoryButton2;

        public static AmazingNewAccessoryLogic Instance;

        public static ConfigEntry<float> UIScaleModifier;

        void Awake()
        {
            MakerAPI.MakerBaseLoaded += createMakerInteractables;
            Logger = base.Logger;


            CharacterApi.RegisterExtraBehaviour<AnalCharaController>(GUID);
            Instance = this;

            AccessoriesApi.AccessoryTransferred += AccessoryTransferred;
            AccessoriesApi.AccessoriesCopied += AccessoriesCopied;

            UIScaleModifier = Config.Bind("UI", "UI Scale Factor", Screen.height <= 1080 ? 1.3f : 1f, new ConfigDescription("Additional Scale to apply to the UI", new AcceptableValueRange<float>(0.5f, 2f)));
        }

        void Start()
        {
            if ( StudioAPI.InsideStudio)
            {
                StudioLoaded();
            }
        }

        private void StudioLoaded()
        {
            CurrentStateCategory currentStateCategory = StudioAPI.GetOrCreateCurrentStateCategory(null);
            currentStateCategory.AddControl(
                new CurrentStateCategorySwitch("Show ANAL", 
                c => c.GetChaControl().GetComponent<AnalCharaController>().displayGraph)).Value.Subscribe(
                display =>
                {
                    if (!display) StudioAPI.GetSelectedControllers<AnalCharaController>().Do(controller => controller.Hide());
                    else StudioAPI.GetSelectedControllers<AnalCharaController>().Do(controller => controller.Show(Input.GetKey(KeyCode.LeftShift)));
                });
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
            if (b)
            {
                MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>()?.Show(Input.GetKey(KeyCode.LeftShift));
            }
            else MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>()?.Hide();
        }

        private void createMakerInteractables(object sender, RegisterCustomControlsEvent e)
        {
            SidebarToggle = e.AddSidebarControl(new SidebarToggle("Show ANAL", false, this));
            SidebarToggle.ValueChanged.Subscribe(delegate (bool b) {
                showGraphInMaker(b);
            });

            AccessoryButton = MakerAPI.AddAccessoryWindowControl(new MakerButton("Create ANAL Output", null, this));
            AccessoryButton.GroupingID = "Buttons";
            AccessoryButton.OnClick.AddListener(() =>
            {
                showGraphInMaker(true);
                MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>()?.addOutput(AccessoriesApi.SelectedMakerAccSlot);
            });

            AccessoryButton2 = MakerAPI.AddAccessoryWindowControl(new MakerButton("Create ANAL Input", null, this));
            AccessoryButton2.GroupingID = "Buttons";
            AccessoryButton2.OnClick.AddListener(() =>
            {
                showGraphInMaker(true);
                AnalCharaController analCharaController = MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>();
                analCharaController?.addAdvanedInputAccessory(AccessoriesApi.SelectedMakerAccSlot, analCharaController.lfg.getSize()/2);
            });
        }

        internal CursorManager getMakerCursorMangaer()
        {
            return base.gameObject.GetComponent<CursorManager>();
        }
    }
}
