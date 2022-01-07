using Buttplug;
using MelonLoader;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UIExpansionKit.API;
using UnityEngine;
using Vibrator_Controller;
using System.Reflection;
using System.Collections.Generic;
using VRC;
using Friend_Notes;
using VRChatUtilityKit.Ui;
using VRChatUtilityKit.Utilities;
using UnhollowerRuntimeLib;
using UnityEngine.UI;
using VRCWSLibary;
using System.Runtime.InteropServices;

[assembly: MelonInfo(typeof(VibratorController), "Vibrator Controller", "1.5.4", "MarkViews", "https://github.com/markviews/VRChatVibratorController")]
[assembly: MelonGame("VRChat", "VRChat")]
[assembly: MelonAdditionalDependencies("UIExpansionKit", "VRCWSLibary", "VRChatUtilityKit")]

namespace Vibrator_Controller {
    public class VibratorController : MelonMod {

        private static bool _useActionMenu = false;
        public static int buttonStep;
        private static GameObject QuickMenu { get; set; }
        public static TabButton TabButton { get; private set; }
        private static ToggleButton _search;
        private static Label _networkStatus;
        private static Label _buttplugError;

        private static MelonPreferences_Category _vibratorController;
        private static ButtplugClient _bpClient;


        public static AssetBundle iconsAssetBundle;
        public static Texture2D logo;
        public static int[] availablePurcent = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        public static Dictionary<int, Texture2D> purcentIcons = new Dictionary<int, Texture2D>();
        public static string[] availableToys = { "Ambi", "Osci", "Edge", "Domi", "Hush", "Nora", "Lush", "Max", "Diamo" };
        public static Dictionary<string, Texture2D> toyIcons = new Dictionary<string, Texture2D>();

        public static bool vgbPresent = false;

        //https://gitlab.com/jacefax/vibegoesbrrr/-/blob/master/VibeGoesBrrrMod.cs#L27
        public static class NativeMethods
        {
            public static string TempPath
            {
                get
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), $"VibratorController-1");
                    if (!Directory.Exists(tempPath))
                    {
                        Directory.CreateDirectory(tempPath);
                    }
                    return tempPath;
                }
            }

            [DllImport("kernel32.dll")]
            public static extern IntPtr LoadLibrary(string dllToLoad);

            public static string LoadUnmanagedLibraryFromResource(Assembly assembly, string libraryResourceName, string libraryName)
            {
                string assemblyPath = Path.Combine(TempPath, libraryName);

                MelonLogger.Msg($"Unpacking and loading {libraryName}");

                using (Stream s = assembly.GetManifestResourceStream(libraryResourceName))
                {
                    var data = new BinaryReader(s).ReadBytes((int)s.Length);
                    File.WriteAllBytes(assemblyPath, data);
                }

                LoadLibrary(assemblyPath);

                return assemblyPath;
            }
        }

        static VibratorController()
        {
            //Clean up old file, call as earlöy as possible so file isnt loaded by VibeGoBrr
            if (File.Exists(Environment.CurrentDirectory + @"\buttplug_rs_ffi.dll"))
                File.Delete(Environment.CurrentDirectory + @"\buttplug_rs_ffi.dll");
            try
            {
                //Adapted from knah's JoinNotifier mod found here: https://github.com/knah/VRCMods/blob/master/JoinNotifier/JoinNotifierMod.cs 
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vibrator_Controller.icons"))
                using (var tempStream = new MemoryStream((int)stream.Length))
                {
                    stream.CopyTo(tempStream);
                    iconsAssetBundle = AssetBundle.LoadFromMemory_Internal(tempStream.ToArray(), 0);
                    iconsAssetBundle.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                }

                logo = iconsAssetBundle.LoadAsset_Internal("Assets/logo.png", Il2CppType.Of<Texture2D>()).Cast<Texture2D>();
                logo.hideFlags |= HideFlags.DontUnloadUnusedAsset;

                foreach (string toyName in availableToys)
                {
                    var logo = iconsAssetBundle.LoadAsset_Internal($"Assets/{toyName.ToLower()}-x64.png", Il2CppType.Of<Texture2D>()).Cast<Texture2D>();
                    logo.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    toyIcons.Add(toyName, logo);
                }

                foreach (int purcent in availablePurcent)
                {
                    var logo = iconsAssetBundle.LoadAsset_Internal($"Assets/{purcent}.png", Il2CppType.Of<Texture2D>()).Cast<Texture2D>();
                    logo.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    purcentIcons.Add(purcent, logo);
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Consider checking for newer version as mod possibly no longer working, Exception occured OnAppStart(): " + e.Message);
            }
        }

        public override void OnApplicationStart()
        {
            if (MelonHandler.Mods.Any(mod => mod.Info.Name == "VibeGoesBrrr"))
            {
                LoggerInstance.Warning("VibeGoesBrrr detected. Disabling Vibrator Controller since these mods are incompatible");
                return;
            }

            
            NativeMethods.LoadUnmanagedLibraryFromResource(Assembly.GetExecutingAssembly(), "Vibrator_Controller.buttplug_rs_ffi.dll", "buttplug_rs_ffi.dll");

            _vibratorController = MelonPreferences.CreateCategory("VibratorController");

            MelonPreferences.CreateEntry(_vibratorController.Identifier, "ActionMenu", true, "action menu integration");
            MelonPreferences.CreateEntry(_vibratorController.Identifier, "buttonStep", 5, "What % to change when pressing button");

            _useActionMenu = MelonPreferences.GetEntryValue<bool>(_vibratorController.Identifier, "ActionMenu");
            buttonStep = MelonPreferences.GetEntryValue<int>(_vibratorController.Identifier, "buttonStep");

            if (_useActionMenu && MelonHandler.Mods.Any(mod => mod.Info.Name == "ActionMenuApi")) {
                try {
                    new ToyActionMenu(LoggerInstance);
                } catch (Exception) {
                    LoggerInstance.Warning("Failed to add action menu button");
                }
            }

            VrcwsIntegration.Init(this);
            MelonCoroutines.Start(UiManagerInitializer());
            CreateButton();

            VRCUtils.OnUiManagerInit += CreateMenu;
        }

        public IEnumerator UiManagerInitializer() {
            while (VRCUiManager.prop_VRCUiManager_0 == null) yield return null;

            QuickMenu = GameObject.Find("UserInterface/Canvas_QuickMenu(Clone)");

            NetworkManagerHooks.Initialize();
            NetworkManagerHooks.OnLeave += OnPlayerLeft;
        }

        private void CreateButton() {
            ExpansionKitApi.GetExpandedMenu(ExpandedMenu.UserQuickMenu).AddSimpleButton("Get\nToys", () => {
                string name = GameObject.Find("UserInterface/Canvas_QuickMenu(Clone)/Container/Window/QMParent/Menu_SelectedUser_Local").GetComponent<VRC.UI.Elements.Menus.SelectedUserMenuQM>().field_Private_IUser_0.prop_String_0;
                VrcwsIntegration.SendMessage(new VibratorControllerMessage(name, Commands.GetToys));
            });

        }

        private void OnPlayerLeft(Player obj) {
            foreach (RemoteToy toy in Toys.RemoteToys.Where(x=>x.Value.connectedTo == obj.prop_String_0).Select(x=>x.Value)) {
                toy.Disable();
            }
        }

        internal void CreateMenu()
        {
            LoggerInstance.Msg("Creating BP client");
            SetupBp();

            LoggerInstance.Msg("Creating Menu");
            _search = new ToggleButton((state) =>
            {
                if (state)
                {
                    _search.Text = "Scanning...";
                    _bpClient.StartScanningAsync();
                }
                else
                {
                    _search.Text = "Scan for toys";
                    _bpClient.StopScanningAsync();
                }
            },
            CreateSpriteFromTexture2D(logo), null, "Scan for toys", "BPToggle", "Scan for connected toys", "Scaning for connected toys");
            _networkStatus = new Label("Network", Client.ClientAvailable() ? "Connected" : "Not\nConnected", "networkstatus");
            _networkStatus.TextComponent.fontSize = 24;
            _buttplugError = new Label("Buttplug", "No Error", "status");
            _buttplugError.TextComponent.fontSize = 24;
            Client.GetClient().ConnectRecieved += async() => {
                await AsyncUtils.YieldToMainThread();
                _networkStatus.SubtitleText = Client.ClientAvailable() ? "Connected" : "Not\nConnected"; 
            };
            TabButton = new TabButton(CreateSpriteFromTexture2D(logo), "Vibrator Controller", "VibratorControllerMenu", "Vibrator Controller", "Vibrator Controller Menu");
            TabButton.SubMenu
              .AddButtonGroup(new ButtonGroup("ControlsGrp", "Controls", new List<IButtonGroupElement>()
              {_search, _networkStatus, _buttplugError
            }));

            //Control all toys (vibrate only)
            new AllControlToy(TabButton.SubMenu, LoggerInstance);

            //activate scroll
            TabButton.SubMenu.ToggleScrollbar(true);
        }

        public static Sprite CreateSpriteFromTexture2D(Texture2D texture)
        {
            if (texture == null) 
                return null;
            Rect size = new Rect(0, 0, texture.width, texture.height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            return Sprite.CreateSprite(texture, size, pivot, 100, 0, SpriteMeshType.Tight, Vector4.zero, false);
        }

        private void SetupBp() {
            _bpClient = new ButtplugClient("VRCVibratorController");
            _bpClient.ConnectAsync(new ButtplugEmbeddedConnectorOptions());
            _bpClient.DeviceAdded += async(object aObj, DeviceAddedEventArgs args) => {
                await AsyncUtils.YieldToMainThread();
                new ButtplugToy(args.Device, TabButton.SubMenu, LoggerInstance);
            };
            
            _bpClient.DeviceRemoved += async(object aObj, DeviceRemovedEventArgs args) => {
                await AsyncUtils.YieldToMainThread();
                if (Toys.MyToys.ContainsKey(args.Device.Index))
                {
                    Toys.MyToys[args.Device.Index].Disable();
                }
            };

            _bpClient.ErrorReceived += async(object aObj, ButtplugExceptionEventArgs args) =>
            {
                LoggerInstance.Msg($"Buttplug Client received error: {args.Exception.Message}");
                await AsyncUtils.YieldToMainThread();

                _buttplugError.SubtitleText = "Error occurred";
            };
        }

        public override void OnUpdate() {
            

            foreach (Toys toy in Toys.AllToys) {
                if (toy.hand == Hand.Shared || toy.hand == Hand.None || toy.hand == Hand.Actionmenu) return;
                
                if (MenuOpen()) return;

                int left = (int)(toy.maxSpeed * Input.GetAxis("Oculus_CrossPlatform_PrimaryIndexTrigger"));
                int right = (int)(toy.maxSpeed * Input.GetAxis("Oculus_CrossPlatform_SecondaryIndexTrigger"));

                switch (toy.hand) {
                    case Hand.Left:
                        right = left;
                        break;
                    case Hand.Right:
                        left = right;
                        break;
                    case Hand.Either:
                        if (left > right) right = left;
                        else left = right;
                        break;
                    case Hand.Both:
                        break;
                }
                if (toy.supportsTwoVibrators) {
                    toy.SetEdgeSpeed(right);
                }
                toy.SetSpeed(left);
            }
        }

        //message from server
        internal async void Message(VibratorControllerMessage msg, string userId) {
            await AsyncUtils.YieldToMainThread();

            switch (msg.Command)
            {
                case Commands.GetToys:
                    HandleGetToys(userId);
                    break;
                case Commands.ToyUpdate:
                    HandleToyUpdate(msg, userId);
                    break;
                case Commands.SetSpeeds:
                    HandleSetSpeeds(msg);
                    break;
            }
        }

        private void HandleSetSpeeds(VibratorControllerMessage msg)
        {
            foreach (var toymessage in msg.messages.Select(x => x.Value))
            {
                if (!Toys.MyToys.ContainsKey(toymessage.ToyId))
                    continue;

                Toys toy = Toys.MyToys[toymessage.ToyId];

                switch (toymessage.Command)
                {
                    //Local toy commands
                    case Commands.SetSpeed:
                        if (toy?.hand == Hand.Shared)
                            toy?.SetSpeed(toymessage.Strength);

                        break;
                    case Commands.SetSpeedEdge:
                        if (toy?.hand == Hand.Shared)
                            toy?.SetEdgeSpeed(toymessage.Strength);

                        break;
                    case Commands.SetAir:
                        if (toy?.hand == Hand.Shared)
                            toy?.SetContraction(toymessage.Strength);

                        break;
                    case Commands.SetRotate:
                        if (toy?.hand == Hand.Shared)
                            toy?.Rotate();

                        break;
                }
            }
        }

        private void HandleToyUpdate(VibratorControllerMessage msg, string userId)
        {
            foreach (var toy in msg.messages.Select(x => x.Value))
            {
                switch (toy.Command)
                {

                    //remote toy commands
                    case Commands.AddToy:

                        LoggerInstance.Msg($"Adding : {toy.ToyName} : {toy.ToyId}");
                        new RemoteToy(toy.ToyName, toy.ToyId, userId, toy.ToyMaxSpeed, toy.ToyMaxSpeed2, toy.ToyMaxLinear, toy.ToySupportsRotate, TabButton.SubMenu, LoggerInstance);

                        break;
                    case Commands.RemoveToy:

                        if (Toys.RemoteToys.ContainsKey(toy.ToyId))
                            Toys.RemoteToys[toy.ToyId].Disable();
                        break;
                }
            }
        }

        private void HandleGetToys(string userId)
        {
            LoggerInstance.Msg("Control Client requested toys");
            VibratorControllerMessage messageToSend = null;
            foreach (KeyValuePair<ulong, ButtplugToy> entry in Toys.MyToys.Where(x => x.Value.hand == Hand.Shared))
            {
                entry.Value.connectedTo = userId;
                if (messageToSend == null)
                    messageToSend = new VibratorControllerMessage(userId, Commands.AddToy, entry.Value);
                else
                    messageToSend.Merge(new VibratorControllerMessage(userId, Commands.AddToy, entry.Value));

            }

            if (messageToSend != null)
                VrcwsIntegration.SendMessage(messageToSend);
        }

        private static bool MenuOpen() {
            if (QuickMenu == null) {
                QuickMenu = GameObject.Find("UserInterface/Canvas_QuickMenu(Clone)");
                return true;
            }

            if (QuickMenu.activeSelf) {
                return true;
            }
                
            return false;
        }
    }
}