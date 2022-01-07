using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace VelNetUnity
{
	public class NetworkGUI : MonoBehaviour
	{
		[FormerlySerializedAs("networkManager")] public VelNetManager velNetManager;
		public InputField userInput;
		public InputField sendInput;
		public InputField roomInput;
		public Text messages;
		public List<string> messageBuffer;
		public Dropdown microphones;
		Dissonance.DissonanceComms comms;

		public void HandleSend()
		{
			if (sendInput.text != "")
			{
				velNetManager.SendTo(VelNetManager.MessageType.OTHERS, sendInput.text);
			}
		}

		public void HandleLogin()
		{
			if (userInput.text != "")
			{
				velNetManager.Login(userInput.text, "nopass");
			}
		}

		public void HandleJoin()
		{
			if (roomInput.text != "")
			{
				velNetManager.Join(roomInput.text);
			}
		}

		// Start is called before the first frame update
		private void Start()
		{
			comms = FindObjectOfType<Dissonance.DissonanceComms>();
			microphones.AddOptions(new List<string>(Microphone.devices));
			velNetManager.MessageReceived += (m) =>
			{
				string s = m.type + ":" + m.sender + ":" + m.text;
				messageBuffer.Add(s);
				messages.text = "";


				if (messageBuffer.Count > 10)
				{
					messageBuffer.RemoveAt(0);
				}

				foreach (string msg in messageBuffer)
				{
					messages.text = messages.text + msg + "\n";
				}
			};
		}

		public void handleMicrophoneSelection()
		{
			comms.MicrophoneName = microphones.options[microphones.value].text;
		}
	}
}