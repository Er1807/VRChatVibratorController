using MelonLoader;
using Buttplug;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine.XR;
using VRChatUtilityKit.Ui;
using UnityEngine;
using System.Threading.Tasks;
using VRChatUtilityKit.Utilities;
using VRC;

namespace Vibrator_Controller {
    public enum Hand {
        None, Shared, Left, Right, Both, Either, Actionmenu
    }

    public interface IToy
    {
        void SetSpeedInternal();
        void SetEdgeSpeedInternal();
        void SetContractionInternal();
        void RotateInternal();
        void UpdateBatteryInternal();
        void EnableInternal();
        void DisableInternal();
        void ChangeHandInternal();

    }

    public abstract class Toys : IToy
    {
        public Toys(MelonLogger.Instance loggerInstance)
        {
            this.LoggerInstance = loggerInstance;
        }

        internal static Dictionary<ulong, RemoteToy> RemoteToys { get; set; } = new Dictionary<ulong, RemoteToy>();
        internal static Dictionary<ulong, ButtplugToy> MyToys { get; set; } = new Dictionary<ulong, ButtplugToy>();
        internal static List<Toys> AllToys => RemoteToys.Select(x => x.Value as Toys).Union(MyToys.Select(x => x.Value as Toys)).ToList();

        public MelonLogger.Instance LoggerInstance { get; }

        internal SingleButton changeMode;
        internal SingleButton inc;
        internal SingleButton dec;
        internal Label label;

        internal ButtonGroup toys;
        internal SubMenu menu;

        internal Hand hand = Hand.None;
        internal string name;
        internal ulong id;
        internal bool isActive = true;

        internal int lastSpeed = 0, lastEdgeSpeed = 0, lastContraction = 0;

        internal bool supportsRotate = false, supportsLinear = false, supportsTwoVibrators = false, supportsBatteryLvl = false;
        internal int maxSpeed = 20, maxSpeed2 = -1, maxLinear = -1;
        internal double battery = -1;
        internal bool clockwise = false;

        public void CreateMenu()
        {
            toys = new ButtonGroup("Toy" + id, name);
            int step = (int)(maxSpeed * ((float)VibratorController.buttonStep / 100));


            changeMode = new SingleButton(ChangeHandInternal, VibratorController.CreateSpriteFromTexture2D(GetTexture()), $"Mode\n{hand}", "mode", "Change Mode");
            inc = new SingleButton(() => { if (lastSpeed + step <= maxSpeed) SetSpeed(lastSpeed + step); }, VibratorController.CreateSpriteFromTexture2D(GetTexture()), "Inc", "inc", "Increment Speed");
            dec = new SingleButton(() => { if (lastSpeed - step >= 0) SetSpeed(lastSpeed - step); }, VibratorController.CreateSpriteFromTexture2D(GetTexture()), "Dec", "dec", "Decrement Speed");
            label = new Label($"Current Speed: {lastSpeed}", "Battery not available", "BatteryStatus");

            label.TextComponent.fontSize = 24;
            toys.AddButton(changeMode);
            toys.AddButton(inc);
            toys.AddButton(dec);
            toys.AddButton(label);

            menu.AddButtonGroup(toys);


            //fix if added after init phase
            toys.gameObject.transform.localScale = Vector3.one;
            toys.Header.gameObject.transform.localScale = Vector3.one;

            toys.gameObject.transform.localRotation = Quaternion.Euler(0, 0, 0);
            toys.Header.gameObject.transform.localRotation = Quaternion.Euler(0, 0, 0);

            var pos = toys.gameObject.transform.localPosition;
            var pos2 = toys.Header.gameObject.transform.localPosition;

            toys.gameObject.transform.localPosition = new Vector3(0, pos.y, 0);
            toys.Header.gameObject.transform.localPosition = new Vector3(0, pos2.y, 0);

        }

        public Texture2D GetTexture() => VibratorController.toyIcons.ContainsKey(name) ? VibratorController.toyIcons[name] : VibratorController.logo;

        public void Disable()
        {
            if (isActive)
            {
                isActive = false;
                LoggerInstance.Msg("Disabled toy: " + name);
                hand = Hand.None;
                toys.rectTransform.gameObject.active = false;
                toys.Header.gameObject.active = false;
                
                DisableInternal();
            }
        }

        public void Enable()
        {
            if (!isActive)
            {
                isActive = true;
                toys.rectTransform.gameObject.active = true;
                toys.Header.gameObject.active = true;

                EnableInternal();

                
                LoggerInstance.Msg("Enabled toy: " + name);
            }
        }

        public void SetSpeed(int speed)
        {
            if (speed != lastSpeed)
            {
                lastSpeed = speed;
                label.Text = $"Current Speed: {speed}";

                SetSpeedInternal();
            }
        }
        public void SetEdgeSpeed(int speed)
        {
            if (speed != lastEdgeSpeed)
            {
                lastEdgeSpeed = speed;
                SetEdgeSpeedInternal();
            }
        }
        public void SetContraction(int speed)
        {
            if (lastContraction != speed)
            {
                lastContraction = speed;
                SetContractionInternal();
            }
        }
        public void Rotate()
        {
            RotateInternal();
        }

        public abstract void DisableInternal();
        public abstract void EnableInternal();
        public abstract void SetContractionInternal();
        public abstract void SetEdgeSpeedInternal();
        public abstract void SetSpeedInternal();
        public abstract void RotateInternal();
        public abstract void UpdateBatteryInternal();
        public abstract void ChangeHandInternal();
    }

    public class ButtplugToy : Toys
    {

        public string connectedTo;
        public ButtplugClientDevice device;

        internal ButtplugToy(ButtplugClientDevice device, SubMenu menu, MelonLogger.Instance loggerInstance) : base(loggerInstance)
        {
            this.menu = menu;
            id = (device.Index + (ulong)Player.prop_Player_0.prop_String_0.GetHashCode()) % long.MaxValue;
            hand = Hand.Shared;
            name = device.Name;
            this.device = device;

            //remove company name
            if (name.Split(' ').Length > 1) name = name.Split(' ')[1];

            if (MyToys.ContainsKey(id))
            {
                loggerInstance.Msg("Device reconnected: " + name + " [" + id + "]");
                MyToys[id].name = name; //id should be uniquie but just to be sure
                MyToys[id].device = device;
                MyToys[id].Enable();
                return;
            }



            loggerInstance.Msg("Device connected: " + name + " [" + id + "]");

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.LinearCmd))
                supportsLinear = true;

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.RotateCmd))
                supportsRotate = true;


            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.BatteryLevelCmd))
            {
                supportsBatteryLvl = true;
                UpdateBatteryInternal();

            }

            //prints info about the device
            foreach (KeyValuePair<ServerMessage.Types.MessageAttributeType, ButtplugMessageAttributes> entry in device.AllowedMessages)
                loggerInstance.Msg("[" + id + "] Allowed Message: " + entry.Key);

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.VibrateCmd))
            {
                ButtplugMessageAttributes attributes = device.AllowedMessages[ServerMessage.Types.MessageAttributeType.VibrateCmd];

                if (attributes.ActuatorType != null && attributes.ActuatorType.Length > 0)
                    loggerInstance.Msg("[" + id + "] ActuatorType " + string.Join(", ", attributes.ActuatorType));

                if (attributes.StepCount != null && attributes.StepCount.Length > 0)
                {
                    loggerInstance.Msg("[" + id + "] StepCount " + string.Join(", ", attributes.StepCount));
                    maxSpeed = (int)attributes.StepCount[0];
                }
                if (attributes.StepCount != null && attributes.StepCount.Length == 2)
                {
                    supportsTwoVibrators = true;
                    maxSpeed2 = (int)attributes.StepCount[1];
                }

                if (attributes.Endpoints != null && attributes.Endpoints.Length > 0)
                    loggerInstance.Msg("[" + id + "] Endpoints " + string.Join(", ", attributes.Endpoints));

                if (attributes.MaxDuration != null && attributes.MaxDuration.Length > 0)
                    loggerInstance.Msg("[" + id + "] MaxDuration " + string.Join(", ", attributes.MaxDuration));

                if (attributes.Patterns != null && attributes.Patterns.Length > 0)
                    foreach (string[] pattern in attributes.Patterns)
                        loggerInstance.Msg("[" + id + "] Pattern " + string.Join(", ", pattern));
            }

            MyToys.Add(id, this);
            CreateMenu();

            if (hand == Hand.Shared)
            {
                VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.AddToy, this));
            }
        }

        public override void ChangeHandInternal()
        {
            if (!isActive) return;

            hand++;
            if (hand > Enum.GetValues(typeof(Hand)).Cast<Hand>().Max())
                hand = 0;
            
            if (hand == Hand.Both && !supportsTwoVibrators)
                hand++;

            if (!XRDevice.isPresent && (hand == Hand.Both || hand == Hand.Either || hand == Hand.Left || hand == Hand.Right))
                hand = Hand.Actionmenu;

            
            if (hand == Hand.Shared)
            {
                VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.AddToy, this));
            }
            else
            {
                VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.RemoveToy, this));
            }
            
            changeMode.Text = "Mode\n" + hand;
        }

        public override void DisableInternal()
        {
            VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.RemoveToy, this));
        }

        public override void EnableInternal()
        {
            if (supportsBatteryLvl)
            {
                UpdateBatteryInternal();
            }
            if (hand == Hand.Shared)
            {
                VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.AddToy, this));
            }
        }

        public override void RotateInternal()
        {
            try
            {
                clockwise = !clockwise;
                device.SendRotateCmd(lastSpeed, clockwise);
            }
            catch (ButtplugDeviceException)
            {
                LoggerInstance.Error("Toy not connected");
            }
        }

        public override void SetContractionInternal()
        {
            try
            {
                //moves to new position in 1 second
                device.SendLinearCmd(1000, (double)lastContraction / maxLinear);
            }
            catch (ButtplugDeviceException)
            {
                LoggerInstance.Error("Toy not connected");
            }
        }

        public override void SetEdgeSpeedInternal()
        {
            try
            {
                device.SendVibrateCmd(new List<double> { (double)lastSpeed / maxSpeed, (double)lastEdgeSpeed / maxSpeed2 });
            }
            catch (ButtplugDeviceException)
            {
                LoggerInstance.Error("Toy not connected");
            }
        }

        public override void SetSpeedInternal()
        {
            try
            {
                if (supportsTwoVibrators)
                    device.SendVibrateCmd(new List<double> { (double)lastSpeed / maxSpeed, (double)lastEdgeSpeed / maxSpeed2 });
                else
                    device.SendVibrateCmd((double)lastSpeed / maxSpeed);
                
            }
            catch (ButtplugDeviceException)
            {
                LoggerInstance.Error("Toy not connected");
            }
        }

        public override async void UpdateBatteryInternal()
        {
            try
            {
                while (isActive)
                {
                    battery = await device.SendBatteryLevelCmd();
                    await AsyncUtils.YieldToMainThread();
                    if (label != null)
                        label.SubtitleText = $"Battery: {battery * 100}";
                    await Task.Delay(1000 * 10);
                }
            }
            catch (Exception)
            {
                //maybe device dissconnected during cmd
            }
        }
    }

    public class RemoteToy : Toys
    {
        public string connectedTo;
        internal RemoteToy(string name, ulong id, string connectedTo, int maxSpeed, int maxSpeed2, int maxLinear, bool supportsRotate, SubMenu menu, MelonLogger.Instance loggerInstance) : base(loggerInstance)
        {
            this.menu = menu;
            if (RemoteToys.ContainsKey(id))
            {
                loggerInstance.Msg("Device reconnected: " + name + " [" + id + "]");
                if (maxSpeed2 != -1) RemoteToys[id].supportsTwoVibrators = true;
                if (maxLinear != -1) RemoteToys[id].supportsLinear = true;
                RemoteToys[id].name = name;
                RemoteToys[id].connectedTo = connectedTo;
                RemoteToys[id].supportsRotate = supportsRotate;
                RemoteToys[id].maxSpeed = maxSpeed;
                RemoteToys[id].maxSpeed2 = maxSpeed2;
                RemoteToys[id].maxLinear = maxLinear;
                RemoteToys[id].Enable();
                loggerInstance.Msg($"Reconnected toy Name: {RemoteToys[id].name}, ID: {RemoteToys[id].id} Max Speed: {RemoteToys[id].maxSpeed}" + (RemoteToys[id].supportsTwoVibrators ? $", Max Speed 2: {RemoteToys[id].maxSpeed2}" : "") + (RemoteToys[id].supportsLinear ? $", Max Linear Speed: {RemoteToys[id].maxLinear}" : "") + (RemoteToys[id].supportsRotate ? $", Supports Rotation" : ""));
                return;
            }

            if (maxSpeed2 != -1) supportsTwoVibrators = true;
            if (maxLinear != -1) supportsLinear = true;

            this.supportsRotate = supportsRotate;
            this.maxSpeed = maxSpeed;
            this.maxSpeed2 = maxSpeed2;
            this.maxLinear = maxLinear;
            this.name = name;
            this.connectedTo = connectedTo;
            this.id = id;

            loggerInstance.Msg($"Added toy Name: {name}, ID: {id} Max Speed: {maxSpeed}" + (supportsTwoVibrators ? $", Max Speed 2: {maxSpeed2}" : "") + (supportsLinear ? $", Max Linear Speed: {maxLinear}" : "") + (supportsRotate ? $", Supports Rotation" : ""));

            RemoteToys.Add(id, this);
            CreateMenu();

        }

        public override void ChangeHandInternal()
        {
            if (!isActive) return;

            hand++;
            if (hand > Enum.GetValues(typeof(Hand)).Cast<Hand>().Max())
                hand = 0;

            if (hand == Hand.Shared)
                hand++;
            if (hand == Hand.Both && !supportsTwoVibrators)
                hand++;

            if (!XRDevice.isPresent && (hand == Hand.Both || hand == Hand.Either || hand == Hand.Left || hand == Hand.Right))
                hand = Hand.Actionmenu;
            
            changeMode.Text = "Mode\n" + hand;
        }

        public override void DisableInternal()
        {
        }

        public override void EnableInternal()
        {
        }

        public override void RotateInternal()
        {
            VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.SetRotate, this));
        }

        public override void SetContractionInternal()
        {
            VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.SetAir, this, lastContraction));
        }

        public override void SetEdgeSpeedInternal()
        {
            VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.SetSpeedEdge, this, lastEdgeSpeed));
        }

        public override void SetSpeedInternal()
        {
            VrcwsIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.SetSpeed, this, lastSpeed));
        }

        public override void UpdateBatteryInternal()
        {
        }
    }

    public class AllControlToy : Toys
    {
        internal AllControlToy(SubMenu menu, MelonLogger.Instance loggerInstance) : base(loggerInstance)
        {
            this.menu = menu;
            
            
            maxSpeed = 20;
            name = "All Toys";
            id = 1000;

            loggerInstance.Msg($"Added toy Name: {name}, ID: {id} Max Speed: {maxSpeed}" + (supportsTwoVibrators ? $", Max Speed 2: {maxSpeed2}" : "") + (supportsLinear ? $", Max Linear Speed: {maxLinear}" : "") + (supportsRotate ? $", Supports Rotation" : ""));
            
            CreateMenu();

        }

        public override void DisableInternal()
        {
        }

        public override void ChangeHandInternal()
        {
        }

        public override void UpdateBatteryInternal()
        {
        }

        public override void EnableInternal()
        {
        }

        public override void SetContractionInternal()
        {
        }

        public override void RotateInternal()
        {
        }

        public override void SetEdgeSpeedInternal()
        {
        }

        public override void SetSpeedInternal()
        {
            foreach (var toy in AllToys)
            {   
                toy.SetSpeed(lastSpeed);
                if (toy.supportsTwoVibrators)
                    toy.SetEdgeSpeed(lastSpeed);
            }
        }
    }
}