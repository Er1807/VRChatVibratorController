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
        none, shared, left, right, both, either, actionmenu
    }

    public interface IToy
    {
        void setSpeedInternal();
        void setEdgeSpeedInternal();
        void setContractionInternal();
        void rotateInternal();
        void UpdateBatteryInternal();
        void enableInternal();
        void disableInternal();
        void changeHandInternal();

    }

    public abstract class Toys : IToy
    {
        public Toys(MelonLogger.Instance LoggerInstance)
        {
            this.LoggerInstance = LoggerInstance;
        }

        internal static Dictionary<ulong, RemoteToy> remoteToys { get; set; } = new Dictionary<ulong, RemoteToy>();
        internal static Dictionary<ulong, ButtplugToy> myToys { get; set; } = new Dictionary<ulong, ButtplugToy>();
        internal static List<Toys> allToys => remoteToys.Select(x => x.Value as Toys).Union(myToys.Select(x => x.Value as Toys)).ToList();

        public MelonLogger.Instance LoggerInstance { get; }

        internal SingleButton changeMode;
        internal SingleButton inc;
        internal SingleButton dec;
        internal Label label;

        internal ButtonGroup toys;
        internal SubMenu menu;

        internal Hand hand = Hand.none;
        internal string name;
        internal ulong id;
        internal bool isActive = true;

        internal int lastSpeed = 0, lastEdgeSpeed = 0, lastContraction = 0;

        internal bool supportsRotate = false, supportsLinear = false, supportsTwoVibrators = false, supportsBatteryLVL = false;
        internal int maxSpeed = 20, maxSpeed2 = -1, maxLinear = -1;
        internal double battery = -1;
        internal bool clockwise = false;

        public void createMenu()
        {
            toys = new ButtonGroup("Toy" + id, name);
            int step = (int)(maxSpeed * ((float)VibratorController.buttonStep / 100));


            changeMode = new SingleButton(changeHandInternal, VibratorController.CreateSpriteFromTexture2D(GetTexture()), $"Mode\n{hand}", "mode", "Change Mode");
            inc = new SingleButton(() => { if (lastSpeed + step <= maxSpeed) setSpeed(lastSpeed + step); }, VibratorController.CreateSpriteFromTexture2D(GetTexture()), "Inc", "inc", "Increment Speed");
            dec = new SingleButton(() => { if (lastSpeed - step >= 0) setSpeed(lastSpeed - step); }, VibratorController.CreateSpriteFromTexture2D(GetTexture()), "Dec", "dec", "Decrement Speed");
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

        public Texture2D GetTexture() => VibratorController.toy_icons.ContainsKey(name) ? VibratorController.toy_icons[name] : VibratorController.logo;

        public void disable()
        {
            if (isActive)
            {
                isActive = false;
                LoggerInstance.Msg("Disabled toy: " + name);
                hand = Hand.none;
                toys.rectTransform.gameObject.active = false;
                toys.Header.gameObject.active = false;
                
                disableInternal();
            }
        }

        public void enable()
        {
            if (!isActive)
            {
                isActive = true;
                toys.rectTransform.gameObject.active = true;
                toys.Header.gameObject.active = true;

                enableInternal();

                
                LoggerInstance.Msg("Enabled toy: " + name);
            }
        }

        public void setSpeed(int speed)
        {
            if (speed != lastSpeed)
            {
                lastSpeed = speed;
                label.Text = $"Current Speed: {speed}";

                setSpeedInternal();
            }
        }
        public void setEdgeSpeed(int speed)
        {
            if (speed != lastEdgeSpeed)
            {
                lastEdgeSpeed = speed;
                setEdgeSpeedInternal();
            }
        }
        public void setContraction(int speed)
        {
            if (lastContraction != speed)
            {
                lastContraction = speed;
                setContractionInternal();
            }
        }
        public void rotate()
        {
            rotateInternal();
        }

        public abstract void disableInternal();
        public abstract void enableInternal();
        public abstract void setContractionInternal();
        public abstract void setEdgeSpeedInternal();
        public abstract void setSpeedInternal();
        public abstract void rotateInternal();
        public abstract void UpdateBatteryInternal();
        public abstract void changeHandInternal();
    }

    public class ButtplugToy : Toys
    {

        public string connectedTo;
        public ButtplugClientDevice device;

        internal ButtplugToy(ButtplugClientDevice device, SubMenu menu, MelonLogger.Instance LoggerInstance) : base(LoggerInstance)
        {
            this.menu = menu;
            id = (device.Index + (ulong)Player.prop_Player_0.prop_String_0.GetHashCode()) % long.MaxValue;
            hand = Hand.shared;
            name = device.Name;
            this.device = device;

            //remove company name
            if (name.Split(' ').Length > 1) name = name.Split(' ')[1];

            if (myToys.ContainsKey(id))
            {
                LoggerInstance.Msg("Device reconnected: " + name + " [" + id + "]");
                myToys[id].name = name; //id should be uniquie but just to be sure
                myToys[id].device = device;
                myToys[id].enable();
                return;
            }



            LoggerInstance.Msg("Device connected: " + name + " [" + id + "]");

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.LinearCmd))
                supportsLinear = true;

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.RotateCmd))
                supportsRotate = true;


            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.BatteryLevelCmd))
            {
                supportsBatteryLVL = true;
                UpdateBatteryInternal();

            }

            //prints info about the device
            foreach (KeyValuePair<ServerMessage.Types.MessageAttributeType, ButtplugMessageAttributes> entry in device.AllowedMessages)
                LoggerInstance.Msg("[" + id + "] Allowed Message: " + entry.Key);

            if (device.AllowedMessages.ContainsKey(ServerMessage.Types.MessageAttributeType.VibrateCmd))
            {
                ButtplugMessageAttributes attributes = device.AllowedMessages[ServerMessage.Types.MessageAttributeType.VibrateCmd];

                if (attributes.ActuatorType != null && attributes.ActuatorType.Length > 0)
                    LoggerInstance.Msg("[" + id + "] ActuatorType " + string.Join(", ", attributes.ActuatorType));

                if (attributes.StepCount != null && attributes.StepCount.Length > 0)
                {
                    LoggerInstance.Msg("[" + id + "] StepCount " + string.Join(", ", attributes.StepCount));
                    maxSpeed = (int)attributes.StepCount[0];
                }
                if (attributes.StepCount != null && attributes.StepCount.Length == 2)
                {
                    supportsTwoVibrators = true;
                    maxSpeed2 = (int)attributes.StepCount[1];
                }

                if (attributes.Endpoints != null && attributes.Endpoints.Length > 0)
                    LoggerInstance.Msg("[" + id + "] Endpoints " + string.Join(", ", attributes.Endpoints));

                if (attributes.MaxDuration != null && attributes.MaxDuration.Length > 0)
                    LoggerInstance.Msg("[" + id + "] MaxDuration " + string.Join(", ", attributes.MaxDuration));

                if (attributes.Patterns != null && attributes.Patterns.Length > 0)
                    foreach (string[] pattern in attributes.Patterns)
                        LoggerInstance.Msg("[" + id + "] Pattern " + string.Join(", ", pattern));
            }

            myToys.Add(id, this);
            createMenu();

            if (hand == Hand.shared)
            {
                VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.AddToy, this));
            }
        }

        public override void changeHandInternal()
        {
            if (!isActive) return;

            hand++;
            if (hand > Enum.GetValues(typeof(Hand)).Cast<Hand>().Max())
                hand = 0;
            
            if (hand == Hand.both && !supportsTwoVibrators)
                hand++;

            if (!XRDevice.isPresent && (hand == Hand.both || hand == Hand.either || hand == Hand.left || hand == Hand.right))
                hand = Hand.actionmenu;

            
            if (hand == Hand.shared)
            {
                VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.AddToy, this));
            }
            else
            {
                VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.RemoveToy, this));
            }
            
            changeMode.Text = "Mode\n" + hand;
        }

        public override void disableInternal()
        {
            VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.RemoveToy, this));
        }

        public override void enableInternal()
        {
            if (supportsBatteryLVL)
            {
                UpdateBatteryInternal();
            }
            if (hand == Hand.shared)
            {
                VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.AddToy, this));
            }
        }

        public override void rotateInternal()
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

        public override void setContractionInternal()
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

        public override void setEdgeSpeedInternal()
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

        public override void setSpeedInternal()
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
        internal RemoteToy(string name, ulong id, string connectedTo, int maxSpeed, int maxSpeed2, int maxLinear, bool supportsRotate, SubMenu menu, MelonLogger.Instance LoggerInstance) : base(LoggerInstance)
        {
            this.menu = menu;
            if (remoteToys.ContainsKey(id))
            {
                LoggerInstance.Msg("Device reconnected: " + name + " [" + id + "]");
                if (maxSpeed2 != -1) remoteToys[id].supportsTwoVibrators = true;
                if (maxLinear != -1) remoteToys[id].supportsLinear = true;
                remoteToys[id].name = name;
                remoteToys[id].connectedTo = connectedTo;
                remoteToys[id].supportsRotate = supportsRotate;
                remoteToys[id].maxSpeed = maxSpeed;
                remoteToys[id].maxSpeed2 = maxSpeed2;
                remoteToys[id].maxLinear = maxLinear;
                remoteToys[id].enable();
                LoggerInstance.Msg($"Reconnected toy Name: {remoteToys[id].name}, ID: {remoteToys[id].id} Max Speed: {remoteToys[id].maxSpeed}" + (remoteToys[id].supportsTwoVibrators ? $", Max Speed 2: {remoteToys[id].maxSpeed2}" : "") + (remoteToys[id].supportsLinear ? $", Max Linear Speed: {remoteToys[id].maxLinear}" : "") + (remoteToys[id].supportsRotate ? $", Supports Rotation" : ""));
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

            LoggerInstance.Msg($"Added toy Name: {name}, ID: {id} Max Speed: {maxSpeed}" + (supportsTwoVibrators ? $", Max Speed 2: {maxSpeed2}" : "") + (supportsLinear ? $", Max Linear Speed: {maxLinear}" : "") + (supportsRotate ? $", Supports Rotation" : ""));

            remoteToys.Add(id, this);
            createMenu();

        }

        public override void changeHandInternal()
        {
            if (!isActive) return;

            hand++;
            if (hand > Enum.GetValues(typeof(Hand)).Cast<Hand>().Max())
                hand = 0;

            if (hand == Hand.shared)
                hand++;
            if (hand == Hand.both && !supportsTwoVibrators)
                hand++;

            if (!XRDevice.isPresent && (hand == Hand.both || hand == Hand.either || hand == Hand.left || hand == Hand.right))
                hand = Hand.actionmenu;
            
            changeMode.Text = "Mode\n" + hand;
        }

        public override void disableInternal()
        {
        }

        public override void enableInternal()
        {
        }

        public override void rotateInternal()
        {
            VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.SetRotate, this));
        }

        public override void setContractionInternal()
        {
            VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.SetAir, this, lastContraction));
        }

        public override void setEdgeSpeedInternal()
        {
            VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.SetSpeedEdge, this, lastEdgeSpeed));
        }

        public override void setSpeedInternal()
        {
            VRCWSIntegration.SendMessage(new VibratorControllerMessage(connectedTo, Commands.SetSpeed, this, lastSpeed));
        }

        public override void UpdateBatteryInternal()
        {
        }
    }

    public class AllControlToy : Toys
    {
        internal AllControlToy(SubMenu menu, MelonLogger.Instance LoggerInstance) : base(LoggerInstance)
        {
            this.menu = menu;
            
            
            maxSpeed = 20;
            name = "All Toys";
            id = 1000;

            LoggerInstance.Msg($"Added toy Name: {name}, ID: {id} Max Speed: {maxSpeed}" + (supportsTwoVibrators ? $", Max Speed 2: {maxSpeed2}" : "") + (supportsLinear ? $", Max Linear Speed: {maxLinear}" : "") + (supportsRotate ? $", Supports Rotation" : ""));
            
            createMenu();

        }

        public override void disableInternal()
        {
        }

        public override void changeHandInternal()
        {
        }

        public override void UpdateBatteryInternal()
        {
        }

        public override void enableInternal()
        {
        }

        public override void setContractionInternal()
        {
        }

        public override void rotateInternal()
        {
        }

        public override void setEdgeSpeedInternal()
        {
        }

        public override void setSpeedInternal()
        {
            foreach (var toy in allToys)
            {   
                toy.setSpeed(lastSpeed);
                if (toy.supportsTwoVibrators)
                    toy.setEdgeSpeed(lastSpeed);
            }
        }
    }
}