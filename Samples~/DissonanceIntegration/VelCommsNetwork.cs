using System;
using Dissonance;
using Dissonance.Networking;
using UnityEngine;
using UnityEngine.Serialization;


namespace VelNetUnity
{
	[AddComponentMenu("VelNetUnity/Dissonance/VelNet Comms Network")]
	public class VelCommsNetwork : MonoBehaviour, ICommsNetwork
	{
		public ConnectionStatus Status => manager.connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
		public NetworkMode Mode => NetworkMode.Client;

		public event Action<NetworkMode> ModeChanged;
		public event Action<string, CodecSettings> PlayerJoined;
		public event Action<string> PlayerLeft;
		public event Action<VoicePacket> VoicePacketReceived;
		public event Action<TextMessage> TextPacketReceived;
		public event Action<string> PlayerStartedSpeaking;
		public event Action<string> PlayerStoppedSpeaking;
		public event Action<RoomEvent> PlayerEnteredRoom;
		public event Action<RoomEvent> PlayerExitedRoom;

		private ConnectionStatus _status = ConnectionStatus.Disconnected;
		private CodecSettings initSettings;
		public string dissonanceId;
		[FormerlySerializedAs("comms")] public DissonanceComms dissonanceComms;
		private NetworkManager manager;

		/// <summary>
		/// listen to this if you want to send voice
		/// </summary>
		public Action<ArraySegment<byte>> VoiceQueued;


		// Start is called before the first frame update
		private void Start()
		{
			_status = ConnectionStatus.Connected;
			dissonanceComms = GetComponent<DissonanceComms>();
			manager = NetworkManager.instance;
		}

		public void Initialize(string playerName, Rooms rooms, PlayerChannels playerChannels, RoomChannels roomChannels, CodecSettings codecSettings)
		{
			dissonanceId = playerName;
			initSettings = codecSettings;
			dissonanceComms.ResetMicrophoneCapture();
		}

		public void VoiceReceived(string sender, byte[] data)
		{
			uint sequenceNumber = BitConverter.ToUInt32(data, 0);
			VoicePacket vp = new VoicePacket(sender, ChannelPriority.Default, 1, true, new ArraySegment<byte>(data, 4, data.Length - 4), sequenceNumber);
			VoicePacketReceived?.Invoke(vp);
		}

		public void SendText(string data, ChannelType recipientType, string recipientId)
		{
			Debug.Log("sending text");
		}

		public void SendVoice(ArraySegment<byte> data)
		{
			VoiceQueued?.Invoke(data);
		}

		public void SetPlayerJoined(string id)
		{
			Debug.Log("Dissonance player joined");
			PlayerJoined?.Invoke(id, initSettings);
			RoomEvent re = new RoomEvent
			{
				Joined = true,
				Room = "Global",
				PlayerName = id
			};
			PlayerEnteredRoom?.Invoke(re);
		}

		public void SetPlayerLeft(string id)
		{
			RoomEvent re = new RoomEvent
			{
				Joined = false,
				Room = "Global",
				PlayerName = id
			};
			PlayerExitedRoom?.Invoke(re);
			// only send this event for non-local players
			if (id != dissonanceId) PlayerLeft?.Invoke(id);
		}

		public void SetPlayerStartedSpeaking(string id)
		{
			PlayerStartedSpeaking?.Invoke(id);
		}

		public void SetPlayerStoppedSpeaking(string id)
		{
			PlayerStoppedSpeaking?.Invoke(id);
		}
	}
}