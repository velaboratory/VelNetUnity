using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dissonance;
using UnityEngine;

namespace VelNetUnity
{
	[RequireComponent(typeof(VelCommsNetwork))]
	[AddComponentMenu("VelNetUnity/Dissonance/VelNet Dissonance Player")]
	public class VelNetDissonancePlayer : NetworkObject, IDissonancePlayer
	{
		private VelCommsNetwork comms;
		private bool isSpeaking;
		private uint lastAudioId;

		public string dissonanceID = "";

		//required by dissonance for spatial audio
		public string PlayerId => dissonanceID;
		public Vector3 Position => transform.position;
		public Quaternion Rotation => transform.rotation;
		public NetworkPlayerType Type => owner.isLocal ? NetworkPlayerType.Local : NetworkPlayerType.Remote;
		public bool IsTracking => true;

		public Vector3 targetPosition;
		public Quaternion targetRotation;

		public List<VelNetDissonancePlayer> allPlayers = new List<VelNetDissonancePlayer>();
		public List<int> closePlayers = new List<int>();

		[Tooltip("Maximum distance to transmit voice data. 0 to always send voice to all players.")]
		public float maxDistance;

		// Start is called before the first frame update
		private void Start()
		{
			comms = FindObjectOfType<VelCommsNetwork>();

			if (!allPlayers.Contains(this))
			{
				allPlayers.Add(this);
			}

			if (owner.isLocal)
			{
				SetDissonanceID(comms.dissonanceId);
				comms.VoiceQueued += SendVoiceData;

				//we also need to know when other players join, so we can send the dissonance ID again

				NetworkManager.instance.OnPlayerJoined += (player) =>
				{
					byte[] b = Encoding.UTF8.GetBytes(dissonanceID);
					owner.SendMessage(this, "d", b);
				};
				NetworkManager.instance.SetupMessageGroup("close", closePlayers.ToArray());
			}
		}

		public override void HandleMessage(string identifier, byte[] message)
		{
			switch (identifier)
			{
				case "a": //audio data
				{
					if (isSpeaking)
					{
						comms.VoiceReceived(dissonanceID, message);
					}

					break;
				}
				case "d": //dissonance id (player joined)
				{
					if (dissonanceID == "") // I don't have this yet
					{
						dissonanceID = Encoding.UTF8.GetString(message);
						// tell the comms network that this player joined the channel
						comms.SetPlayerJoined(dissonanceID); // tell dissonance
						comms.comms.TrackPlayerPosition(this); // tell dissonance to track the remote player
					}

					break;
				}
				case "x": // speaking state
				{
					if (message[0] == 0)
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

		private void SendVoiceData(ArraySegment<byte> data)
		{
			// need to send it
			if (owner == null || !owner.isLocal) return;

			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write(BitConverter.GetBytes(lastAudioId++));
			writer.Write(data.ToArray());
			// send voice data unreliably
			owner.SendGroupMessage(this, "close", "a", mem.ToArray(), false);
		}

		/// <summary>
		/// This sort of all initializes dissonance
		/// </summary>
		/// <param name="id">Dissonance ID</param>
		public void SetDissonanceID(string id)
		{
			dissonanceID = id;
			byte[] b = Encoding.UTF8.GetBytes(dissonanceID);
			owner.SendMessage(this, "d", b);
			comms.comms.TrackPlayerPosition(this);
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
			if (owner == null) return;
			if (!owner.isLocal) return;

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
					if (dist < maxDistance && !closePlayers.Contains(p.owner.userid))
					{
						closePlayers.Add(p.owner.userid);
						closePlayerListChanged = true;
					}
					else if (dist >= maxDistance && closePlayers.Contains(p.owner.userid))
					{
						closePlayers.Remove(p.owner.userid);
						closePlayerListChanged = true;
					}
				}

				if (closePlayerListChanged)
				{
					NetworkManager.instance.SetupMessageGroup("close", closePlayers);
				}
			}


			//handle dissonance comms

			//if we're not speaking, and the comms say we are, send a speaking event, which will be received on other network players and sent to their comms accordingly
			if (comms.comms.FindPlayer(dissonanceID)?.IsSpeaking != isSpeaking) //unfortunately, there does not seem to be an event for this
			{
				isSpeaking = !isSpeaking;
				byte[] toSend = { isSpeaking ? (byte)1 : (byte)0 };
				owner.SendMessage(this, "x", toSend);

				if (!isSpeaking)
				{
					lastAudioId = 0;
				}
			}
		}
	}
}