using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dissonance;
using UnityEngine;

namespace VelNet
{
	/// <summary>
	/// This should be added to your player object
	/// </summary>
	[AddComponentMenu("VelNet/Dissonance/VelNet Dissonance Player")]
	public class VelNetDissonancePlayer : NetworkComponent, IDissonancePlayer
	{
		private VelCommsNetwork comms;
		private bool isSpeaking;
		private uint lastAudioId;

		[Header("VelNet Dissonance Player Properties")]
		public string dissonanceID = "";

		//required by dissonance for spatial audio
		public string PlayerId => dissonanceID;
		public Vector3 Position => transform.position;
		public Quaternion Rotation => transform.rotation;
		public NetworkPlayerType Type => IsMine ? NetworkPlayerType.Local : NetworkPlayerType.Remote;
		public bool IsTracking => true;

		private static readonly List<VelNetDissonancePlayer> allPlayers = new List<VelNetDissonancePlayer>();

		/// <summary>
		/// Only sends voice data to players in this list
		/// </summary>
		public List<int> closePlayers = new List<int>();

		[Tooltip("Maximum distance to transmit voice data. 0 to always send voice to all players.")]
		public float maxDistance;

		private enum MessageType : byte
		{
			AudioData,
			DissonanceId,
			SpeakingState
		}

		// This object should not be in the scene at the start.
		private void Awake()
		{
			comms = FindObjectOfType<VelCommsNetwork>();
			if (comms == null)
			{
				Debug.LogError("No VelCommsNetwork found. Make sure there is one in your scene.", this);
			}
		}

		private void OnEnable()
		{
			// add ourselves to the global list of all players in the scene
			if (!allPlayers.Contains(this))
			{
				allPlayers.Add(this);
			}
			else
			{
				Debug.LogError("We're already in the player list 🐭", this);
			}
		}

		private void OnDisable()
		{
			// remove ourselves from the global list of all players in the scene
			allPlayers.Remove(this);
		}

		private void Start()
		{
			if (IsMine)
			{
				SetDissonanceID(comms.dissonanceId);
				comms.VoiceQueued += SendVoiceData;

				//we also need to know when other players join, so we can send the dissonance ID again

				VelNetManager.instance.OnPlayerJoined += (player) =>
				{
					using MemoryStream mem = new MemoryStream();
					using BinaryWriter writer = new BinaryWriter(mem);
					writer.Write((byte)MessageType.DissonanceId);
					writer.Write(dissonanceID);
					SendBytes(mem.ToArray());
				};
			}
		}

		private void SendVoiceData(ArraySegment<byte> data)
		{
			// need to send it
			if (!IsMine) return;

			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.AudioData);
			writer.Write(lastAudioId++);
			writer.Write(data.ToArray());
			// send voice data unreliably
			// SendBytes(mem.ToArray(), false);
			SendBytesToGroup("voice", mem.ToArray());
		}

		/// <summary>
		/// This sort of all initializes dissonance
		/// </summary>
		/// <param name="id">Dissonance ID</param>
		public void SetDissonanceID(string id)
		{
			dissonanceID = id;

			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.DissonanceId);
			writer.Write(dissonanceID);
			SendBytes(mem.ToArray());
			comms.dissonanceComms.TrackPlayerPosition(this);
		}

		private void VoiceInitialized(string id)
		{
			dissonanceID = id;
		}

		private void OnDestroy()
		{
			comms.SetPlayerLeft(dissonanceID);
		}


		// Update is called once per frame
		private void Update()
		{
			if (!IsMine) return;

			// handle nearness cutoff
			if (maxDistance > 0)
			{
				bool closePlayerListChanged = false;
				foreach (VelNetDissonancePlayer p in allPlayers)
				{
					if (p == this)
					{
						continue;
					}

					float dist = Vector3.Distance(p.transform.position, transform.position);
					if (dist < maxDistance && !closePlayers.Contains(p.Owner.userid))
					{
						closePlayers.Add(p.Owner.userid);
						closePlayerListChanged = true;
					}
					else if (dist >= maxDistance && closePlayers.Contains(p.Owner.userid))
					{
						closePlayers.Remove(p.Owner.userid);
						closePlayerListChanged = true;
					}
				}

				if (closePlayerListChanged)
				{
					VelNetManager.instance.SetupMessageGroup("voice", closePlayers);
				}
			}
			else
			{
				int lastLength = closePlayers.Count;
				closePlayers = allPlayers.Where(p => p != this).Select(p => p.Owner.userid).ToList();
				if (closePlayers.Count != lastLength)
				{
					VelNetManager.instance.SetupMessageGroup("voice", closePlayers);
				}
			}


			// handle dissonance comms

			//if we're not speaking, and the comms say we are, send a speaking event, which will be received on other network players and sent to their comms accordingly
			if (comms.dissonanceComms.FindPlayer(dissonanceID)?.IsSpeaking != isSpeaking) //unfortunately, there does not seem to be an event for this
			{
				isSpeaking = !isSpeaking;

				using MemoryStream mem = new MemoryStream();
				using BinaryWriter writer = new BinaryWriter(mem);
				writer.Write((byte)MessageType.SpeakingState);
				writer.Write(isSpeaking ? (byte)1 : (byte)0);
				SendBytes(mem.ToArray());

				if (!isSpeaking)
				{
					lastAudioId = 0;
				}
			}
		}

		public override void ReceiveBytes(byte[] message)
		{
			using MemoryStream mem = new MemoryStream(message);
			using BinaryReader reader = new BinaryReader(mem);

			byte identifier = reader.ReadByte();
			switch (identifier)
			{
				case 0: //audio data
				{
					if (isSpeaking)
					{
						comms.VoiceReceived(dissonanceID, message.Skip(1).ToArray());
					}

					break;
				}
				case 1: //dissonance id (player joined)
				{
					if (dissonanceID == "") // I don't have this yet
					{
						dissonanceID = reader.ReadString();

						// tell the comms network that this player joined the channel
						comms.SetPlayerJoined(dissonanceID); // tell dissonance
						comms.dissonanceComms.TrackPlayerPosition(this); // tell dissonance to track the remote player
					}

					break;
				}
				case 2: // speaking state
				{
					if (message[1] == 0)
					{
						comms.SetPlayerStoppedSpeaking(dissonanceID);
						isSpeaking = false;
					}
					else
					{
						comms.SetPlayerStartedSpeaking(dissonanceID);
						isSpeaking = true;
					}

					break;
				}
			}
		}
	}
}