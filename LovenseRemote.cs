﻿using MelonLoader;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Windows.Forms;
using UnityEngine;

namespace Lovense_Remote {
    public class LovenseRemote : MelonMod {

        public static ArrayList toys = new ArrayList();
        string findButton = null;
        bool lockSpeed = false;
        bool requireHold = false;
        public static QMNestedButton menu;
        public static QMSingleButton LockButtonUI;
        QMSingleButton LockKeyBind;
        QMSingleButton HoldButtonUI;
        QMSingleButton HoldKeyBind;
        QMSingleButton addButtonUI;
        QMToggleButton holdToggle;
        KeyCode lockButton;//button to lock speed
        KeyCode holdButton;//button to hold with other controll to use toy (if enabled)
        public static string subMenu;
        int buttonX;
        int buttonY;

        public override void OnApplicationStart() {
            MelonPrefs.RegisterCategory("LovenseRemote", "Lovense Remote");
            MelonPrefs.RegisterInt("LovenseRemote", "lockButton", 0, "Button to lock speed");
            MelonPrefs.RegisterInt("LovenseRemote", "holdButton", 0, "Button to hold to use toy");
            MelonPrefs.RegisterBool("LovenseRemote", "Requirehold", false, "If enabled you will need to hold set button to use toy");
            MelonPrefs.RegisterString("LovenseRemote", "subMenu", "UIElementsMenu", "Menu to put the mod button on");
            MelonPrefs.RegisterInt("LovenseRemote", "buttonX", 0, "x position to put the mod button");
            MelonPrefs.RegisterInt("LovenseRemote", "buttonY", 0, "y position to put the mod button");

            lockButton = (KeyCode)MelonPrefs.GetInt("LovenseRemote", "lockButton");
            holdButton = (KeyCode)MelonPrefs.GetInt("LovenseRemote", "holdButton");
            requireHold = MelonPrefs.GetBool("LovenseRemote", "Requirehold");
            subMenu = MelonPrefs.GetString("LovenseRemote", "subMenu");
            buttonX = MelonPrefs.GetInt("LovenseRemote", "buttonX");
            buttonY = MelonPrefs.GetInt("LovenseRemote", "buttonY");
        }

        public override void VRChat_OnUiManagerInit() {
            menu = new QMNestedButton(subMenu, buttonX, buttonY, "Lovense\nRemote", "Lovense remote settings");

            LockButtonUI = new QMSingleButton(menu, 1, 0, "Lock Speed\nButton", delegate () {
                if (findButton == "lockButton") {
                    lockButton = KeyCode.None;
                    findButton = null;
                    LockButtonUI.setButtonText("Lock Speed\nButton\nCleared");
                    return;
                }
                findButton = "lockButton";
                LockButtonUI.setButtonText("Press Now");
            }, "Click than press button on controller to set button to lock vibraton speed", null, null);

            // LockKey keybind 
            LockKeyBind = new QMSingleButton(menu, 1, 1, "none", new System.Action(() => {

            }), "Shows current Lock Speed Button keybind", null, null);
            LockKeyBind.getGameObject().GetComponent<RectTransform>().sizeDelta /= new Vector2(1f, 2.0175f);
            LockKeyBind.getGameObject().GetComponent<RectTransform>().anchoredPosition += new Vector2(0f, 96f);
            LockKeyBind.setIntractable(false);

            HoldButtonUI = new QMSingleButton(menu, 2, 0, "Hold\nButton", delegate () {
                if (findButton == "holdButton") {
                    holdButton = KeyCode.None;
                    findButton = null;
                    HoldButtonUI.setButtonText("Hold\nButton\nCleared");
                    return;
                }
                findButton = "holdButton";
                HoldButtonUI.setButtonText("Press Now");
            }, "Click than press button on controller to set button to hold to use toy", null, null);

            // LockKey keybind 
            HoldKeyBind = new QMSingleButton(menu, 2, 1, "none", new System.Action(() => {

            }), "Shows current Hold Button keybind", null, null);
            HoldKeyBind.getGameObject().GetComponent<RectTransform>().sizeDelta /= new Vector2(1f, 2.0175f);
            HoldKeyBind.getGameObject().GetComponent<RectTransform>().anchoredPosition += new Vector2(0f, 96f);
            HoldKeyBind.setIntractable(false);

            addButtonUI = new QMSingleButton(menu, 3, 0, "Add\nToys", delegate () {
                string token = getToken();//gets id from link in clipboard
                string[] idName = getIDandName(token);//name, id
                if (token == null || idName == null) {
                    addButtonUI.setButtonText("Add\nToys\nFailed");
                } else new Toy(idName[0], token, idName[1]);

            }, "Click to paste your friend's Long Distance Control Link code", null, null);

            holdToggle = new QMToggleButton(menu, 5, -1, "Hold on", delegate () {
                HoldButtonUI.setActive(true);
                HoldKeyBind.setActive(true);
                requireHold = true;
                MelonPrefs.SetBool("LovenseRemote", "Requirehold", true);
            }, "Hold off", delegate () {
                HoldButtonUI.setActive(false);
                HoldKeyBind.setActive(false);
                requireHold = false;
                MelonPrefs.SetBool("LovenseRemote", "Requirehold", false);

            }, "Require holding a button to use toy?");

            holdToggle.setToggleState(requireHold);
            HoldButtonUI.setActive(requireHold);

        }

        public override void OnUpdate() {
            if (HoldKeyBind != null) {
                HoldKeyBind.setButtonText(holdButton.ToString());
            }

            if (LockKeyBind != null) {
                LockKeyBind.setButtonText(lockButton.ToString());
            }

            if (findButton != null) getButton();

            if (Input.GetKeyDown(lockButton)) {
                if (lockSpeed) lockSpeed = false;
                else lockSpeed = true;
            }

            if (lockSpeed) return;

            if (requireHold)
                if (!Input.GetKey(holdButton)) {
                    foreach (Toy toy in toys)
                        toy.setSpeed(0);
                    return;
                }

            foreach (Toy toy in toys) {
                float speed = 0;
                switch (toy.hand) {
                    case "none":
                        break;
                    case "left":
                        speed = Input.GetAxis("Oculus_CrossPlatform_PrimaryIndexTrigger");
                        break;
                    case "right":
                        speed = Input.GetAxis("Oculus_CrossPlatform_SecondaryIndexTrigger");
                        break;
                    case "either":
                        float left = Input.GetAxis("Oculus_CrossPlatform_PrimaryIndexTrigger");
                        float right = Input.GetAxis("Oculus_CrossPlatform_SecondaryIndexTrigger");
                        if (left > right) speed = left;
                        else speed = right;
                        break;
                }
                toy.setSpeed(speed);
            }
        }



        public void getButton() {
            //A-Z
            for (int i = 97; i <= 122; i++)
                if (Input.GetKey((KeyCode)i)) {
                    setButton((KeyCode)i);
                    return;
                }

            //left vr controller buttons
            if (Input.GetKey(KeyCode.JoystickButton0)) setButton(KeyCode.JoystickButton0);
            else if (Input.GetKey(KeyCode.JoystickButton1)) setButton(KeyCode.JoystickButton1);
            else if (Input.GetKey(KeyCode.JoystickButton2)) setButton(KeyCode.JoystickButton2);
            else if (Input.GetKey(KeyCode.JoystickButton3)) setButton(KeyCode.JoystickButton3);
            else if (Input.GetKey(KeyCode.JoystickButton8)) setButton(KeyCode.JoystickButton8);
            else if (Input.GetKey(KeyCode.JoystickButton9)) setButton(KeyCode.JoystickButton9);

            //right vr controller buttons
            else if (Input.GetKey(KeyCode.Joystick1Button0)) setButton(KeyCode.Joystick1Button0);
            else if (Input.GetKey(KeyCode.Joystick1Button1)) setButton(KeyCode.Joystick1Button1);
            else if (Input.GetKey(KeyCode.Joystick1Button2)) setButton(KeyCode.Joystick1Button2);
            else if (Input.GetKey(KeyCode.Joystick1Button3)) setButton(KeyCode.Joystick1Button3);
            else if (Input.GetKey(KeyCode.Joystick1Button8)) setButton(KeyCode.Joystick1Button8);
            else if (Input.GetKey(KeyCode.Joystick1Button9)) setButton(KeyCode.Joystick1Button9);
        }

        public void setButton(KeyCode button) {
            if (findButton.Equals("lockButton")) {
                lockButton = button;
                LockButtonUI.setButtonText("Lock Speed\nButton Set");
                MelonPrefs.SetInt("LovenseRemote", "lockButton", button.GetHashCode());
            } else if (findButton.Equals("holdButton")) {
                holdButton = button;
                HoldButtonUI.setButtonText("Hold\nButton Set");
                MelonPrefs.SetInt("LovenseRemote", "holdButton", button.GetHashCode());
            }
            findButton = null;
        }

        static string[] getIDandName(string token) {
            if (token == null) return null;
            var url = "https://c.lovense.com/app/ws2/play/" + token;
            var httpRequest = (HttpWebRequest)WebRequest.Create(url);
            httpRequest.Headers["authority"] = "c.lovense.com";
            httpRequest.Headers["sec-ch-ua"] = "\"Google Chrome\";v=\"87\", \" Not; A Brand\";v=\"99\", \"Chromium\";v=\"87\"";
            httpRequest.Headers["sec-ch-ua-mobile"] = "?0";
            httpRequest.Headers["upgrade-insecure-requests"] = "1";
            httpRequest.Headers["sec-fetch-site"] = "same-origin";
            httpRequest.Headers["sec-fetch-mode"] = "navigate";
            httpRequest.Headers["sec-fetch-user"] = "?1";
            httpRequest.Headers["sec-fetch-dest"] = "document";
            httpRequest.Headers["accept-language"] = "en-US,en;q=0.9";
            httpRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.88 Safari/537.36";
            httpRequest.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";
            httpRequest.Referer = "https://c.lovense.com/app/ws/play/" + token;
            var httpResponse = (HttpWebResponse)httpRequest.GetResponse();
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream())) {
                var result = streamReader.ReadToEnd();
                int start = result.IndexOf("JSON.parse('") + 12;
                int end = result.IndexOf("')");
                if (end == -1) return null;
                JObject json = JObject.Parse(result.Substring(start, end - start));
                if (json.Count == 0) {
                    return null;
                } else {
                    string id = (string)json.First.First["id"];
                    string name = (string)json.First.First["name"];
                    name = char.ToUpper(name[0]) + name.Substring(1);//make first letter uppercase
                    return new string[] { name, id };
                }
            }
        }

        public static string getToken() {
            string url = Clipboard.GetText();
            if (!url.Contains("https://c.lovense.com/c/")) return null;
            HttpWebResponse resp = null;
            try {
                HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(url);
                req.Method = "HEAD";
                req.AllowAutoRedirect = false;
                resp = (HttpWebResponse)req.GetResponse();
                url = resp.Headers["Location"];
            } catch (Exception e) {
                return null;
            } finally {
                if (resp != null) resp.Close();
            }
            int pos = url.LastIndexOf("/") + 1;
            return url.Substring(pos, url.Length - pos);
        }

    }

    public class Toy {
        public string hand = "none";
        private static int x = 1;
        private QMSingleButton button;
        private float lastSpeed;
        private string name;
        private string token;
        private string id;

        public Toy(string name, string token, string id) {
            this.token = token;
            this.id = id;
            this.name = name;
            button = new QMSingleButton(LovenseRemote.menu, x++, 2, name + "\nClick to\nSet", delegate () {
                changeHand();
            }, "Click to set controll mode", null, null);
            LovenseRemote.toys.Add(this);
        }

        public void setSpeed(float speed) {
            speed = (int)(speed * 10);
            if (speed != lastSpeed) {
                lastSpeed = speed;
                send((int)speed);
            }
        }

        public void changeHand() {
            switch (hand) {
                case "none":
                    hand = "left";
                    button.setButtonText(name + "\nLeft Trigger");
                    //LovenseRemote.LockButtonUI.setActive(true);//in case this was disabled
                    break;
                case "left":
                    hand = "right";
                    button.setButtonText(name + "\nRight Trigger");
                    break;
                case "right":
                    hand = "either";
                    button.setButtonText(name + "\nEither Trigger");
                    break;
                /*
            case "either":
                hand = "slider";
                button.setButtonText(name + "\nSlider");
                //disable "Lock Speed Button" button when this is on for all connected toys
                foreach (Toy toy in LovenseRemote.toys)
                    if (toy.hand != "slider" || toy.hand != "none") break;
                LovenseRemote.LockButtonUI.setActive(false);
                    break;
            case "slider":
                */
                case "either":
                    hand = "none";
                    button.setButtonText(name + "\nClick to\nSet");
                    LovenseRemote.LockButtonUI.setActive(true);//in case this was disabled
                    break;
            }
        }

        private void send(int speed) {
            var httpRequest = (HttpWebRequest)WebRequest.Create("https://c.lovense.com/app/ws/command/" + token);
            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/x-www-form-urlencoded";
            using (var streamWriter = new StreamWriter(httpRequest.GetRequestStream())) {
                streamWriter.Write("order=%7B%22cate%22%3A%22id%22%2C%22id%22%3A%7B%22" + id + "%22%3A%7B%22v%22%3A" + speed + "%2C%22p%22%3A-1%2C%22r%22%3A-1%7D%7D%7D");
            }
            var httpResponse = (HttpWebResponse)httpRequest.GetResponse();
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream())) {
                var result = streamReader.ReadToEnd();
                //Console.WriteLine(result);
            }
            //Console.WriteLine(httpResponse.StatusCode);
        }


    }
}