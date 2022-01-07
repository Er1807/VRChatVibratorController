using MelonLoader;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Timers;
using VRC.UI;
using VRCWSLibary;

namespace Vibrator_Controller {
    public enum Commands {
        AddToy, RemoveToy, SetSpeed, SetSpeedEdge, SetAir, SetRotate, GetToys, SetSpeeds, ToyUpdate
    }

    public class ToyMessage
    {
        public Commands Command { get; set; }
        public ulong ToyId { get; set; }
        public string ToyName { get; set; }
        public int Strength { get; set; }
        public int ToyMaxSpeed { get; set; }
        public int ToyMaxSpeed2 { get; set; }
        public int ToyMaxLinear { get; set; }
        public bool ToySupportsRotate { get; set; }
    }

    public class VibratorControllerMessage
    {
        public VibratorControllerMessage() { }
        public VibratorControllerMessage(string target, Commands command) { 
            Target = target; 
            Command = command; 
        }
        public VibratorControllerMessage(string target, Commands command, Toys toy) { 
            Target = target;

            messages[toy.id + ":" + command] = new ToyMessage()
            {
                Command = command,
                ToyId = toy.id,
                ToyName = toy.name,
                ToyMaxSpeed = toy.maxSpeed,
                ToyMaxSpeed2 = toy.maxSpeed2,
                ToyMaxLinear = toy.maxLinear,
                ToySupportsRotate = toy.supportsRotate
            };
            Command = Commands.ToyUpdate;

        }
        public VibratorControllerMessage(string target, Commands command, Toys toy, int strength) { 
            Target = target;
            messages[toy.id +":"+ command] = new ToyMessage()
            {
                Command = command,
                ToyId = toy.id,
                ToyName = toy.name,
                Strength = strength
            };
            Command = Commands.SetSpeeds;
        }

        public string Target { get; set; }
        public Commands Command { get; set; }
        public Dictionary<string, ToyMessage> messages = new Dictionary<string, ToyMessage>();

        //Merge the parameter into the current one
        public void Merge(VibratorControllerMessage otherMessage)
        {
            foreach (var message in otherMessage.messages)
            {
                this.messages[message.Key] = message.Value;
            }
        }

    }
    public class VrcwsIntegration {

        private static Client _client;
        private static MelonPreferences_Entry<bool> _onlyTrusted;

        private static VibratorController _vibratorController;
        private static Dictionary<string, VibratorControllerMessage> _messagesToSendPerTarget = new Dictionary<string, VibratorControllerMessage>();


        public static void Init(VibratorController controller) {
            var category = MelonPreferences.CreateCategory("VibratorController");
            _onlyTrusted = category.CreateEntry("Only Trusted", false);
            MelonCoroutines.Start(LoadClient());
            Timer timer = new Timer(200);
            timer.Elapsed += (_,__) => {
                if (_client == null)
                    return;

                lock (_messagesToSendPerTarget)
                {
                    foreach (var message in _messagesToSendPerTarget)
                    {
                        _client.Send(new Message() { Method = "VibratorControllerMessage", Target = message.Value.Target, Content = JsonConvert.SerializeObject(message.Value) });
                    }
                    _messagesToSendPerTarget.Clear();
                }
            };
            timer.Enabled = true;
            _vibratorController = controller;
        }

        public static void SendMessage(VibratorControllerMessage message) {
            if (_client == null || message.Target == null)
                return;
            if (message.Command == Commands.SetSpeeds)
            {
                lock (_messagesToSendPerTarget)
                {
                    if (_messagesToSendPerTarget.ContainsKey(message.Target))
                        _messagesToSendPerTarget[message.Target].Merge(message);
                    else
                        _messagesToSendPerTarget[message.Target] = message;
                }
                return;
            }

            _client.Send(new Message() { Method = "VibratorControllerMessage", Target = message.Target, Content = JsonConvert.SerializeObject(message) });
        }

        private static IEnumerator LoadClient() {
            while (!Client.ClientAvailable())
                yield return null;


            _client = Client.GetClient();

            _onlyTrusted.OnValueChanged += (_, newValue) => {
                _client.RemoveEvent("VibratorControllerMessage");
                _client.RegisterEvent("VibratorControllerMessage", EventCall, signatureRequired: newValue);
            };


            _client.RegisterEvent("VibratorControllerMessage", EventCall, signatureRequired: _onlyTrusted.Value);

        }

        private static long _lastTick = 0;

        private static void EventCall(Message msg) {
            //MelonLogger.Msg($"VibratorControllerMessage recieved");
            //MelonLogger.Msg(msg);

            if (msg.TimeStamp.Ticks > _lastTick) {
                _lastTick = msg.TimeStamp.Ticks; 
                var messagecontent = msg.GetContentAs<VibratorControllerMessage>();
                if(messagecontent != null)
                    _vibratorController.Message(messagecontent, msg.Target);
            }
        }


    }
}