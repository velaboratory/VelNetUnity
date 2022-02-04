using System.Collections;
using System.Collections.Generic;
using System.Text;
using Dissonance;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace VelNet
{
	public class NetworkGUI : MonoBehaviour
	{
		public VelNetManager velNetManager;
		
		public bool autoConnect = true;

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
				StartCoroutine(testes());
			}
		}

		IEnumerator testes()
		{
			yield return new WaitForSeconds(1.0f);
			HandleLogin();
			yield return new WaitForSeconds(1.0f);
			HandleJoin();
			yield return null;
		}

		public void handleMicrophoneSelection()
		{
			comms.MicrophoneName = microphones.options[microphones.value].text;
		}
	}
}