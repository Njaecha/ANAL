using System;
using UniRx;
using KKAPI;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using KKAPI.Maker;
using KKAPI.Chara;
using KKAPI.Studio;
using IllusionFixes;
using KKAPI.Maker.UI;
using BepInEx.Logging;
using KKAPI.Studio.UI;
using BepInEx.Configuration;
using KKAPI.Maker.UI.Sidebar;
using System.Collections.Generic;
using System.Linq;
using Studio;
using static HandCtrl;

namespace AmazingNewAccessoryLogic
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    //[BepInDependency(MakerOptimizations.GUID)] would prevent use in Studio
    class AmazingNewAccessoryLogic : BaseUnityPlugin
    {
        public const string PluginName = "AmazingNewAccessoryLogic";
        public const string GUID = "org.njaecha.plugins.anal";
        public const string Version = "0.1.2";

        internal new static ManualLogSource Logger;

        public static SidebarToggle SidebarToggle;
        public static MakerButton AccessoryButton;
        public static MakerButton AccessoryButton2;

        public static AmazingNewAccessoryLogic Instance;

        public static ConfigEntry<bool> Debug { get; private set; }
        public static ConfigEntry<float> UIScaleModifier { get; private set; }
        public static ConfigEntry<KeyboardShortcut> ShortcutOpen { get; private set; }

        public static ConfigEntry<KeyCode> UIDeleteNodeKey;
        public static ConfigEntry<KeyCode> UIDisableNodeKey;
        public static ConfigEntry<KeyCode> UISelectedTreeKey;
        public static ConfigEntry<KeyCode> UISelectNetworkKey;


        void Awake()
        {
            MakerAPI.MakerBaseLoaded += createMakerInteractables;
            MakerAPI.ReloadCustomInterface += (sender, args) => UpdateMakerButtonVisibility();
            Logger = base.Logger;


            CharacterApi.RegisterExtraBehaviour<AnalCharaController>(GUID);
            Instance = this;

            AccessoriesApi.AccessoryTransferred += AccessoryTransferred;
            AccessoriesApi.AccessoriesCopied += AccessoriesCopied;
            AccessoriesApi.AccessoryKindChanged += AccessoryKindChanged;
            AccessoriesApi.SelectedMakerAccSlotChanged += (sender, args) => UpdateMakerButtonVisibility();

            Debug = Config.Bind("Advanced", "Debug", false,
                new ConfigDescription("Whether to log detailed debug messages", null,
                    new KKAPI.Utilities.ConfigurationManagerAttributes { IsAdvanced = true }));
            UIScaleModifier = Config.Bind("UI", "UI Scale Factor", Screen.height <= 1080 ? 1.3f : 1f,
                new ConfigDescription("Additional Scale to apply to the UI",
                    new AcceptableValueRange<float>(0.5f, 2f)));
            UIDeleteNodeKey = Config.Bind("Keybinds", "Delete Node", KeyCode.Delete,
                "Key press to delete the selected node(s)");
            UIDeleteNodeKey.SettingChanged += KeyCodeSettingChanged;
            UIDisableNodeKey = Config.Bind("Keybinds", "Disable Node", KeyCode.D,
                "Key press to disable the selected node(s)");
            UIDisableNodeKey.SettingChanged += KeyCodeSettingChanged;
            UISelectedTreeKey = Config.Bind("Keybinds", "Select Tree", KeyCode.T,
                "Key press to expand the selection to all downstream nodes");
            UISelectedTreeKey.SettingChanged += KeyCodeSettingChanged;
            UISelectNetworkKey = Config.Bind("Keybinds", "Select Network", KeyCode.N,
                "Key press to expand the selection to all down and upstream nodes");
            UISelectNetworkKey.SettingChanged += KeyCodeSettingChanged;
            ShortcutOpen = Config.Bind("UI", "Open UI", new KeyboardShortcut(),
                new ConfigDescription("Keyboard shortcut to open / close the ANAL UI"));

            Hooks.SetupHooks();
            
        }

        private void KeyCodeSettingChanged(object sender, EventArgs e)
        {
            CharacterApi.GetRegisteredBehaviour(GUID).Instances
                .Do(ctrl => ((AnalCharaController)ctrl).UpdateGraphKeybinds());
        }

        void Start()
        {
            if (StudioAPI.InsideStudio)
            {
                StudioLoaded();
            }
        }

        void Update()
        {
            if (ShortcutOpen.Value.IsDown())
            {
                if (MakerAPI.InsideMaker)
                {
                    AnalCharaController ctrl = MakerAPI.GetCharacterControl().GetComponent<AnalCharaController>();
                    SidebarToggle.SetValue(!ctrl?.displayGraph ?? false);
                }
                else if (StudioAPI.InsideStudio)
                {
                    List<OCIChar> chars = StudioAPI.GetSelectedCharacters().ToList();
                    if (chars.Count == 0)
                    {
                        Logger.LogMessage("Please select a character!");
                    }
                    else
                    {
                        AnalCharaController ctrl = chars[0].charInfo.GetComponent<AnalCharaController>();
                        if (ctrl?.displayGraph ?? false)
                        {
                            ctrl?.Hide();
                        }
                        else
                        {
                            ctrl?.Show(false);
                        }
                    }
                }
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
                    if (!display)
                        StudioAPI.GetSelectedControllers<AnalCharaController>().Do(controller => controller.Hide());
                    else
                        StudioAPI.GetSelectedControllers<AnalCharaController>().Do(controller =>
                            controller.Show(Input.GetKey(KeyCode.LeftShift)));
                });
            TimelineHelper.PopulateTimeline();
        }

        private void AccessoriesCopied(object sender, AccessoryCopyEventArgs e)
        {
            ChaFileDefine.CoordinateType dType = e.CopyDestination;
            ChaFileDefine.CoordinateType sType = e.CopySource;
            IEnumerable<int> slots = e.CopiedSlotIndexes;
            MakerAPI.GetCharacterControl().gameObject.GetComponent<AnalCharaController>()
                .AccessoriesCopied((int)sType, (int)dType, slots);
        }

        private void AccessoryTransferred(object sender, AccessoryTransferEventArgs e)
        {
            int dSlot = e.DestinationSlotIndex;
            int sSlot = e.SourceSlotIndex;
            MakerAPI.GetCharacterControl().gameObject.GetComponent<AnalCharaController>()
                .AccessoryTransferred(sSlot, dSlot);
        }

        private void AccessoryKindChanged(object sender, AccessorySlotEventArgs e)
        {
            int changedSlot = e.SlotIndex;
            MakerAPI.GetCharacterControl().gameObject.GetComponent<AnalCharaController>()
                .AccessoryKindChanged(changedSlot);
        }

        private void showGraphInMaker(bool b)
        {
            if (b)
            {
                MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>()
                    ?.Show(Input.GetKey(KeyCode.LeftShift));
            }
            else MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>()?.Hide();
        }
        
        internal static void UpdateMakerButtonVisibility()
        {
            if (!MakerAPI.InsideMaker) return;
            AnalCharaController controller = MakerAPI.GetCharacterControl().GetComponent<AnalCharaController>();
            var show = false;
            if (controller) show = controller.IsCurrentAdvanced;
            if (show)
            {
                // slot is selected
                show = AccessoriesApi.SelectedMakerAccSlot != -1 &&
                       // accessory type is not none
                       MakerAPI.GetCharacterControl().nowCoordinate.accessory.parts[AccessoriesApi.SelectedMakerAccSlot]
                           .type != (int)ChaListDefine.CategoryNo.ao_none;
            }
            foreach (GameObject btn in AccessoryButton.ControlObjects.Where(b => b.activeSelf != show))
            {
                btn.SetActive(show);
            }

            foreach (GameObject btn in AccessoryButton2.ControlObjects.Where(b => b.activeSelf != show))
            {
                btn.SetActive(show);
            }
            
            Logger.LogDebug($"Setting MakerButtons to {show}");
        }

        private void createMakerInteractables(object sender, RegisterCustomControlsEvent e)
        {
            SidebarToggle = e.AddSidebarControl(new SidebarToggle("Show ANAL", false, this));
            SidebarToggle.ValueChanged.Subscribe(delegate(bool b) { showGraphInMaker(b); });

            AccessoryButton = MakerAPI.AddAccessoryWindowControl(new MakerButton("Create ANAL Output", null, this));
            AccessoryButton.GroupingID = "Buttons";
            AccessoryButton.OnClick.AddListener(() =>
            {
                showGraphInMaker(true);
                MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>()
                    ?.addOutput(AccessoriesApi.SelectedMakerAccSlot);
            });

            AccessoryButton2 = MakerAPI.AddAccessoryWindowControl(new MakerButton("Create ANAL Input", null, this));
            AccessoryButton2.GroupingID = "Buttons";
            AccessoryButton2.OnClick.AddListener(() =>
            {
                showGraphInMaker(true);
                AnalCharaController analCharaController =
                    MakerAPI.GetCharacterControl()?.GetComponent<AnalCharaController>();
                analCharaController?.addAdvancedInputAccessory(AccessoriesApi.SelectedMakerAccSlot,
                    analCharaController.lfg.getSize() / 2);
            });
            
            UpdateMakerButtonVisibility();
        }

        internal CursorManager getMakerCursorMangaer()
        {
            return base.gameObject.GetComponent<CursorManager>();
        }
    }
}