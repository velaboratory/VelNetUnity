using System;
using Dissonance;
using Dissonance.Networking;
using UnityEngine;


namespace VelNetUnity
{
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
		CodecSettings initSettings;
		public string dissonanceId;
		public DissonanceComms comms;
		public NetworkManager manager;

		public Action<ArraySegment<byte>> voiceQueued = delegate { }; //listen to this if you want to send voice

		public void Initialize(string playerName, Rooms rooms, PlayerChannels playerChannels, RoomChannels roomChannels, CodecSettings codecSettings)
		{
			dissonanceId = playerName;
			initSettings = codecSettings;
			comms.ResetMicrophoneCapture();
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
			voiceQueued(data);
		}

		// Start is called before the first frame update
		private void Start()
		{
			_status = ConnectionStatus.Connected;
			comms = GetComponent<DissonanceComms>();
		}

		public void playerJoined(string id)
		{
			Debug.Log("dissonance player joined");
			PlayerJoined?.Invoke(id, initSettings);
			RoomEvent re = new RoomEvent();
			re.Joined = true;
			re.Room = "Global";
			re.PlayerName = id;
			PlayerEnteredRoom?.Invoke(re);
		}

		public void playerLeft(string id)
		{
			RoomEvent re = new RoomEvent();
			re.Joined = false;
			re.Room = "Global";
			re.PlayerName = id;
			PlayerExitedRoom?.Invoke(re);
			PlayerLeft?.Invoke(id);
		}

		public void playerStartedSpeaking(string id)
		{
			PlayerStartedSpeaking?.Invoke(id);
		}

		public void playerStoppedSpeaking(string id)
		{
			PlayerStoppedSpeaking?.Invoke(id);
		}
	}
}