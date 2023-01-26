using System.Collections.Generic;
using System;
using System.IO;
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


		public VelNetPlayer()
		{
			manager = VelNetManager.instance;
			VelNetManager.OnPlayerJoined += HandlePlayerJoined;
		}

		public void HandlePlayerJoined(VelNetPlayer player, bool alreadyInRoom)
		{
			//if this is the local player, go through the objects that I own, and send instantiation messages for the ones that have prefab names
			if (isLocal)
			{
				foreach (KeyValuePair<string, NetworkObject> kvp in manager.objects)
				{
					if (kvp.Value.owner == this && kvp.Value.prefabName != "")
					{
						if (kvp.Value.instantiatedWithTransform)
						{
							using MemoryStream mem = new MemoryStream();
							using BinaryWriter writer = new BinaryWriter(mem);
							writer.Write((byte)VelNetManager.MessageType.InstantiateWithTransform);
							writer.Write(kvp.Value.networkId);
							writer.Write(kvp.Value.prefabName);
							writer.Write(kvp.Value.initialPosition);
							writer.Write(kvp.Value.initialRotation);
							VelNetManager.SendToRoom(mem.ToArray(), false, true);
						}
						else
						{
							using MemoryStream mem = new MemoryStream();
							using BinaryWriter writer = new BinaryWriter(mem);
							writer.Write((byte)VelNetManager.MessageType.Instantiate);
							writer.Write(kvp.Value.networkId);
							writer.Write(kvp.Value.prefabName);
							VelNetManager.SendToRoom(mem.ToArray(), false, true);	
						}
						
					}
				}

				if (IsMaster)
				{
					//send a list of scene object ids when someone joins
					SendSceneUpdate();
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
		public void HandleMessage(VelNetManager.DataMessage m, bool unknown_sender = false)
		{
			using MemoryStream mem = new MemoryStream(m.data);
			using BinaryReader reader = new BinaryReader(mem);

			//individual message parameters separated by comma
			VelNetManager.MessageType messageType = (VelNetManager.MessageType)reader.ReadByte();

			if (messageType == VelNetManager.MessageType.Custom)
			{
				// Custom packets. These are global data that can be sent from anywhere.
				// Any script can subscribe to the callback to receive the message data.
				// todo: strange hack that any player can handle the custom message, which then simply calls velnetmanager.  

				int len = reader.ReadInt32();
				try
				{
					VelNetManager.CustomMessageReceived?.Invoke(m.senderId, reader.ReadBytes(len));
				}
				catch (Exception e)
				{
					VelNetLogger.Error(e.ToString());
				}

				return;
			}

			if (unknown_sender)
			{
				VelNetLogger.Error("Received non-custom message from player that doesn't exist ");
				return;
			}

			switch (messageType)
			{
				// sync update for an object "I" may own
				// "I" being the person sending
				case VelNetManager.MessageType.ObjectSync:
				{
					string objectKey = reader.ReadString();
					byte componentIdx = reader.ReadByte();
					int messageLength = reader.ReadInt32();
					byte[] syncMessage = reader.ReadBytes(messageLength);
					if (manager.objects.ContainsKey(objectKey))
					{
						bool isRpc = (componentIdx & 1) == 1;
						componentIdx = (byte)(componentIdx >> 1);

						// rpcs can be sent by non-owners
						if (isRpc || manager.objects[objectKey].owner == this)
						{
							manager.objects[objectKey].ReceiveBytes(componentIdx, isRpc, syncMessage);
						}
					}

					break;
				}
				case VelNetManager.MessageType.TakeOwnership: // I'm trying to take ownership of an object 
				{
					string networkId = reader.ReadString();

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
				case VelNetManager.MessageType.Instantiate: // I'm trying to instantiate an object 
				{
					string networkId = reader.ReadString();
					string prefabName = reader.ReadString();
					if (manager.objects.ContainsKey(networkId))
					{
						break; //we already have this one, ignore
					}

					VelNetManager.ActuallyInstantiate(networkId, prefabName, this);

					break;
				}
				case VelNetManager.MessageType.InstantiateWithTransform: // I'm trying to instantiate an object 
				{
					string networkId = reader.ReadString();
					string prefabName = reader.ReadString();
					Vector3 position = reader.ReadVector3();
					Quaternion rotation = reader.ReadQuaternion();
					if (manager.objects.ContainsKey(networkId))
					{
						break; //we already have this one, ignore
					}

					VelNetManager.ActuallyInstantiate(networkId, prefabName, this, position, rotation);

					break;
				}
				case VelNetManager.MessageType.Destroy: // I'm trying to destroy a gameobject I own
				{
					VelNetManager.SomebodyDestroyedNetworkObject(reader.ReadString());
					break;
				}
				case VelNetManager.MessageType.DeleteSceneObjects: // deleted scene objects
				{
					int len = reader.ReadInt32();
					for (int k = 1; k < len; k++)
					{
						VelNetManager.SomebodyDestroyedNetworkObject(reader.ReadString());
					}

					break;
				}

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public void SetAsMasterPlayer()
		{
			IsMaster = true;
			//if I'm master, I'm now responsible for updating all scene objects
			//FindObjectsOfType<NetworkObject>();
		}

		public static bool SendGroupMessage(NetworkObject obj, string group, byte componentIdx, byte[] data, bool reliable = true)
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)VelNetManager.MessageType.ObjectSync);
			writer.Write(obj.networkId);
			writer.Write(componentIdx);
			writer.Write(data.Length);
			writer.Write(data);
			return VelNetManager.SendToGroup(group, mem.ToArray(), reliable);
		}

		public static bool SendMessage(NetworkObject obj, byte componentIdx, byte[] data, bool reliable = true)
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)VelNetManager.MessageType.ObjectSync);
			writer.Write(obj.networkId);
			writer.Write(componentIdx);
			writer.Write(data.Length);
			writer.Write(data);
			return VelNetManager.SendToRoom(mem.ToArray(), false, reliable);
		}

		public void SendSceneUpdate()
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)VelNetManager.MessageType.DeleteSceneObjects);
			writer.Write(manager.deletedSceneObjects.Count);
			foreach (string o in manager.deletedSceneObjects)
			{
				writer.Write(o);
			}

			VelNetManager.SendToRoom(mem.ToArray());
		}

		[Obsolete("Use VelNetManager.NetworkDestroy() instead.")]
		public void NetworkDestroy(string networkId)
		{
			// must be the local owner of the object to destroy it
			if (!manager.objects.ContainsKey(networkId) || manager.objects[networkId].owner != this || !isLocal) return;

			// send to all, which will make me delete as well

			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)VelNetManager.MessageType.Destroy);
			writer.Write(networkId);
			VelNetManager.SendToRoom(mem.ToArray(), true, true);
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
			// Also sent to all players, so that multiple simultaneous requests will result in the same outcome.
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)VelNetManager.MessageType.TakeOwnership);
			writer.Write(networkId);
			VelNetManager.SendToRoom(mem.ToArray(), true, true, ordered: true);

			return true;
		}
	}
}