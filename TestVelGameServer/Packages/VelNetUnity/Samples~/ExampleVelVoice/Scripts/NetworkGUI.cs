using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VelNet
{
	public class NetworkGUI : MonoBehaviour
	{
		public InputField userInput;
		public InputField sendInput;
		public InputField roomInput;
		public Text messages;
		public List<string> messageBuffer;
		public Dropdown microphones;
		public VelVoice velVoice;
		public void HandleLogin()
		{
			if (userInput.text != "")
			{
				VelNetManager.Login(userInput.text, SystemInfo.deviceUniqueIdentifier);
			}
		}

		public void HandleGetRooms()
		{
			if (VelNetManager.instance.connected)
			{
				VelNetManager.GetRooms();
			}
		}

		public void GetRoomData()
		{
			if (VelNetManager.IsConnected)
			{
				VelNetManager.GetRoomData(VelNetManager.Room);
			}
		}

		public void HandleJoin()
		{
			if (roomInput.text != "")
			{
				VelNetManager.Join(roomInput.text);
			}
		}

		public void HandleLeave()
		{
			VelNetManager.Leave();
		}

		// Start is called before the first frame update
		private void Start()
		{
			microphones.AddOptions(new List<string>(Microphone.devices));
			handleMicrophoneSelection();
		}

		public void handleMicrophoneSelection()
		{
			velVoice.startMicrophone(microphones.options[microphones.value].text);
		}
	}
}