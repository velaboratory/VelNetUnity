using System.Collections;
using System.Collections.Generic;
using Dissonance;
using UnityEngine;
using UnityEngine.UI;

namespace VelNet
{
	public class NetworkGUI : MonoBehaviour
	{
		public bool autoConnect = true;
		public bool autoRejoin = true;

		public InputField userInput;
		public InputField sendInput;
		public InputField roomInput;
		public Text messages;
		public List<string> messageBuffer;
		public Dropdown microphones;
		private DissonanceComms comms;


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
				VelNetManager.GetRoomData("0");
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
			comms = FindObjectOfType<DissonanceComms>();
			microphones.AddOptions(new List<string>(Microphone.devices));

			if (autoConnect)
			{
				AutoJoin();
			}
		}

		private void AutoJoin()
		{
			VelNetManager.OnConnectedToServer += Login;

			void Login()
			{
				if (!autoRejoin)
				{
					VelNetManager.OnConnectedToServer -= Login;
				}

				HandleLogin();
				VelNetManager.OnLoggedIn += JoinRoom;

				void JoinRoom()
				{
					HandleJoin();
					VelNetManager.OnLoggedIn -= JoinRoom;
				}
			}
		}

		private void Update()
		{
			if (autoRejoin)
			{
				if (!VelNetManager.IsConnected)
				{
					AutoJoin();
				}
			}
		}

		public void handleMicrophoneSelection()
		{
			comms.MicrophoneName = microphones.options[microphones.value].text;
		}
	}
}