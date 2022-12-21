using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VelNet.Voice;

namespace VelNet
{
	public class MicrophoneSelection : MonoBehaviour
	{
		public Dropdown microphones;
		public VelVoice velVoice;

		private void Start()
		{
			microphones.AddOptions(Microphone.devices.ToList());
		}

		public void HandleMicrophoneSelection()
		{
			velVoice.StartMicrophone(microphones.options[microphones.value].text);
		}
	}
}