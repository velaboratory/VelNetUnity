using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VelNet.Voice;

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
		public InputField hostInput;
		public InputField portInput;


		// Start is called before the first frame update
		private void Start()
		{
#if !UNITY_WEBGL && !UNITY_EDITOR
			microphones.AddOptions(new List<string>(Microphone.devices));
			HandleMicrophoneSelection();
#endif
			hostInput.SetTextWithoutNotify(VelNetManager.instance.host);
			portInput.SetTextWithoutNotify(VelNetManager.instance.port.ToString());
		}


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
				VelNetManager.JoinRoom(roomInput.text);
			}
		}

		public void HandleLeave()
		{
			VelNetManager.LeaveRoom();
		}

		public void HandleMicrophoneSelection()
		{
			velVoice.StartMicrophone(microphones.options[microphones.value].text);
		}

		public void SetPort(string port)
		{
			VelNetManager.SetServer(VelNetManager.instance.host, int.Parse(port));
		}

		public void SetHost(string host)
		{
			VelNetManager.SetServer(host, VelNetManager.instance.port);
		}
	}
}