using System.Collections.Generic;
using System;
using UnityEngine;

namespace VelNet
{
	/// <summary>
	/// Represents a network player
	/// </summary>
	public class VelNetPlayer
	{
		public int userid;
		public string room;

		public bool isLocal;

		private VelNetManager manager;

		/// <summary>
		/// For instantiation. This is not synced across the network
		/// </summary>
		internal int lastObjectId;

		public bool IsMaster { get; private set; }

		// Reusable readers for message handling (main thread only)
		private static readonly NetworkReader messageReader = new NetworkReader();
		private static readonly NetworkReader componentReader = new NetworkReader();

		public VelNetPlayer()
		{
			manager = VelNetManager.instance;
			VelNetManager.OnPlayerJoined += HandlePlayerJoined;
		}

		public void HandlePlayerJoined(VelNetPlayer player, bool alreadyInRoom)
		{
			// if this is the local player, go through the objects that I own, and send instantiation messages for the ones that have prefab names
			if (isLocal)
			{
				NetworkWriter w = VelNetManager.GetSendWriter();
				foreach (KeyValuePair<string, NetworkObject> kvp in manager.objects)
				{
					if (kvp.Value.owner == this && (kvp.Value.prefabName != "" || kvp.Value.isSceneObject))
					{
						w.Reset();
						w.Write((byte)VelNetManager.MessageType.InstantiateWithState);
						w.Write(kvp.Value.networkId);
						w.Write(kvp.Value.prefabName);
						kvp.Value.PackState(w);

						// TODO this sends to everybody in the room, not just the new guy
						VelNetManager.SendToRoom(w.Buffer, 0, w.Length, false, true);
					}
				}

				if (IsMaster)
				{
					//send a list of scene object ids when someone joins
					SendSceneUpdate();
				}

				// tell everybody that I own this object
				foreach (NetworkObject obj in manager.sceneObjects)
				{
					if (obj.IsMine)
					{
						obj.TakeOwnership();
						w.Reset();
						w.Write((byte)VelNetManager.MessageType.ForceState);
						w.Write(obj.networkId);

						obj.PackState(w);
						// TODO this sends to everybody in the room, not just the new guy
						VelNetManager.SendToRoom(w.Buffer, 0, w.Length, false, true);
					}
				}
			}
		}

		/// <summary>
		/// These are generally things that come from the "owner" and should be enacted locally, where appropriate
		///
		/// Message encoding:
		///		byte:	message type
		///		byte[]:	message
		///
		/// The length of the byte[] for message is fixed according to the message type
		/// </summary>
		public void HandleMessage(VelNetManager.DataMessage m, bool unknownSender = false)
		{
			messageReader.SetBuffer(m.data, 0, m.data.Length);

			VelNetManager.MessageType messageType = (VelNetManager.MessageType)messageReader.ReadByte();

			if (messageType == VelNetManager.MessageType.Custom)
			{
				// Custom packets. These are global data that can be sent from anywhere.
				int len = messageReader.ReadInt32();
				try
				{
					VelNetManager.CustomMessageReceived?.Invoke(m.senderId, messageReader.ReadBytes(len));
				}
				catch (Exception e)
				{
					VelNetLogger.Error(e.ToString());
				}

				return;
			}

			if (unknownSender)
			{
				VelNetLogger.Error("Received non-custom message from player that doesn't exist ");
				return;
			}

			switch (messageType)
			{
				// sync update for an object "I" may own
				case VelNetManager.MessageType.ObjectSync:
				{
					string objectKey = messageReader.ReadString();
					byte componentIdx = messageReader.ReadByte();
					int messageLength = messageReader.ReadInt32();
					if (manager.objects.ContainsKey(objectKey))
					{
						bool isRpc = (componentIdx & 1) == 1;
						componentIdx = (byte)(componentIdx >> 1);

						// Set up component reader for exactly this segment
						componentReader.SetBuffer(m.data, messageReader.Position, messageLength);

						// rpcs can be sent by non-owners
						if (isRpc || manager.objects[objectKey].owner == this)
						{
							manager.objects[objectKey].ReceiveBytes(componentIdx, isRpc, componentReader);
						}
					}

					break;
				}
				case VelNetManager.MessageType.TakeOwnership:
				{
					string networkId = messageReader.ReadString();

					if (manager.objects.ContainsKey(networkId))
					{
						manager.objects[networkId].owner = this;
						try
						{
							manager.objects[networkId].OwnershipChanged?.Invoke(this);
						}
						catch (Exception e)
						{
							VelNetLogger.Error("Error in event handling.\n" + e);
						}
					}

					break;
				}
				case VelNetManager.MessageType.Instantiate:
				{
					string networkId = messageReader.ReadString();
					string prefabName = messageReader.ReadString();
					if (manager.objects.ContainsKey(networkId))
					{
						break;
					}

					VelNetManager.ActuallyInstantiate(networkId, prefabName, this);

					break;
				}
				case VelNetManager.MessageType.InstantiateWithTransform:
				{
					string networkId = messageReader.ReadString();
					string prefabName = messageReader.ReadString();
					Vector3 position = messageReader.ReadVector3();
					Quaternion rotation = messageReader.ReadQuaternion();
					if (manager.objects.ContainsKey(networkId))
					{
						break;
					}

					VelNetManager.ActuallyInstantiate(networkId, prefabName, this, position, rotation);

					break;
				}
				case VelNetManager.MessageType.InstantiateWithState:
				{
					string networkId = messageReader.ReadString();
					string prefabName = messageReader.ReadString();
					if (manager.objects.ContainsKey(networkId))
					{
						break;
					}

					NetworkObject networkObject = VelNetManager.ActuallyInstantiate(networkId, prefabName, this);
					networkObject.UnpackState(messageReader);
					break;
				}
				case VelNetManager.MessageType.ForceState:
				{
					string networkId = messageReader.ReadString();

					if (manager.objects.ContainsKey(networkId))
					{
						manager.objects[networkId].UnpackState(messageReader);
					}
					break;
				}
				case VelNetManager.MessageType.Destroy:
				{
					VelNetManager.SomebodyDestroyedNetworkObject(messageReader.ReadString());
					break;
				}
				case VelNetManager.MessageType.DeleteSceneObjects:
				{
					int len = messageReader.ReadInt32();
					for (int k = 1; k < len; k++)
					{
						VelNetManager.SomebodyDestroyedNetworkObject(messageReader.ReadString());
					}

					break;
				}
				case VelNetManager.MessageType.KeepAlive:
				{
					DateTime messageTime = DateTime.FromBinary(messageReader.ReadInt64());
					DateTime currentTime = DateTime.UtcNow;
					VelNetManager.Ping = (int)Math.Round((currentTime - messageTime).TotalMilliseconds);
					break;
				}

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public void SetAsMasterPlayer()
		{
			IsMaster = true;
		}

		public static bool SendGroupMessage(NetworkObject obj, string group, byte componentIdx, byte[] data,
			int offset, int length, bool reliable = true)
		{
			NetworkWriter w = VelNetManager.GetSendWriter();
			w.Reset();
			w.Write((byte)VelNetManager.MessageType.ObjectSync);
			w.Write(obj.networkId);
			w.Write(componentIdx);
			w.Write(length);
			w.Write(data, offset, length);
			return VelNetManager.SendToGroup(group, w.Buffer, 0, w.Length, reliable);
		}

		public static bool SendMessage(NetworkObject obj, byte componentIdx, byte[] data, int offset, int length, bool reliable = true)
		{
			NetworkWriter w = VelNetManager.GetSendWriter();
			w.Reset();
			w.Write((byte)VelNetManager.MessageType.ObjectSync);
			w.Write(obj.networkId);
			w.Write(componentIdx);
			w.Write(length);
			w.Write(data, offset, length);
			return VelNetManager.SendToRoom(w.Buffer, 0, w.Length, false, reliable);
		}

		public void SendSceneUpdate()
		{
			NetworkWriter w = VelNetManager.GetSendWriter();
			w.Reset();
			w.Write((byte)VelNetManager.MessageType.DeleteSceneObjects);
			w.Write(manager.deletedSceneObjects.Count);
			foreach (string o in manager.deletedSceneObjects)
			{
				w.Write(o);
			}

			VelNetManager.SendToRoom(w.Buffer, 0, w.Length);
		}

		[Obsolete("Use VelNetManager.NetworkDestroy() instead.")]
		public void NetworkDestroy(string networkId)
		{
			// must be the local owner of the object to destroy it
			if (!manager.objects.ContainsKey(networkId) || manager.objects[networkId].owner != this || !isLocal) return;

			NetworkWriter w = VelNetManager.GetSendWriter();
			w.Reset();
			w.Write((byte)VelNetManager.MessageType.Destroy);
			w.Write(networkId);
			VelNetManager.SendToRoom(w.Buffer, 0, w.Length, true, true);
		}

		/// <returns>True if successful, False if failed to transfer ownership</returns>
		[Obsolete("Use VelNetManager.TakeOwnership() instead.")]
		public bool TakeOwnership(string networkId)
		{
			// must exist and be the the local player
			if (!manager.objects.ContainsKey(networkId) || !isLocal) return false;

			// if the ownership is locked, fail
			if (manager.objects[networkId].ownershipLocked) return false;

			// immediately successful
			manager.objects[networkId].owner = this;
			try
			{
				manager.objects[networkId].OwnershipChanged?.Invoke(this);
			}
			catch (Exception e)
			{
				VelNetLogger.Error("Error in event handling.\n" + e);
			}

			// must be ordered, so that ownership transfers are not confused.
			NetworkWriter w = VelNetManager.GetSendWriter();
			w.Reset();
			w.Write((byte)VelNetManager.MessageType.TakeOwnership);
			w.Write(networkId);
			VelNetManager.SendToRoom(w.Buffer, 0, w.Length, true, true, ordered: true);

			return true;
		}
	}
}
