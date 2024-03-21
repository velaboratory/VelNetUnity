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
#if !UNITY_WEBGL && !UNITY_EDITOR
			microphones.AddOptions(Microphone.devices.ToList());
#endif
		}

		public void HandleMicrophoneSelection()
		{
#if !UNITY_WEBGL && !UNITY_EDITOR
			velVoice.StartMicrophone(microphones.options[microphones.value].text);
#endif
		}
	}
}