using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
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
		/// For instantiation
		/// </summary>
		public int lastObjectId;


		private bool isMaster;


		public VelNetPlayer()
		{
			manager = VelNetManager.instance;
			VelNetManager.OnPlayerJoined += HandlePlayerJoined;
		}

		public void HandlePlayerJoined(VelNetPlayer player)
		{
			//if this is the local player, go through the objects that I own, and send instantiation messages for the ones that have prefab names
			if (isLocal)
			{
				foreach (KeyValuePair<string, NetworkObject> kvp in manager.objects)
				{
					if (kvp.Value.owner == this && kvp.Value.prefabName != "")
					{
						using MemoryStream mem = new MemoryStream();
						using BinaryWriter writer = new BinaryWriter(mem);
						writer.Write((byte)VelNetManager.MessageType.Instantiate);
						writer.Write(kvp.Value.networkId);
						writer.Write(kvp.Value.prefabName);
						VelNetManager.SendToRoom(mem.ToArray(), false, true);
					}
				}

				if (isMaster)
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
		public void HandleMessage(VelNetManager.DataMessage m)
		{
			using MemoryStream mem = new MemoryStream(m.data);
			using BinaryReader reader = new BinaryReader(mem);
			
			//individual message parameters separated by comma
			VelNetManager.MessageType messageType = (VelNetManager.MessageType)reader.ReadByte();

			switch (messageType)
			{
				case VelNetManager.MessageType.ObjectSync: // sync update for an object I may own
				{
					string objectKey = reader.ReadString();
					byte componentIdx = reader.ReadByte();
					int messageLength = reader.ReadInt32();
					byte[] syncMessage = reader.ReadBytes(messageLength);
					if (manager.objects.ContainsKey(objectKey))
					{
						if (manager.objects[objectKey].owner == this)
						{
							manager.objects[objectKey].ReceiveBytes(componentIdx, syncMessage);
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

					VelNetManager.SomebodyInstantiatedNetworkObject(networkId, prefabName, this);

					break;
				}
				case VelNetManager.MessageType.Destroy: // I'm trying to destroy a gameobject I own
				{
					string networkId = reader.ReadString();

					VelNetManager.NetworkDestroy(networkId);
					break;
				}
				case VelNetManager.MessageType.DeleteSceneObjects: //deleted scene objects
				{
					int len = reader.ReadInt32();
					for (int k = 1; k < len; k++)
					{
						VelNetManager.NetworkDestroy(reader.ReadString());
					}
					break;
				}
				case VelNetManager.MessageType.Custom: // custom packets
				{
					int len = reader.ReadInt32();
					try
					{
						VelNetManager.CustomMessageReceived?.Invoke(m.senderId, reader.ReadBytes(len));
					}
					catch (Exception e)
					{
						Debug.LogError(e);
					}

					break;
				}
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public void SetAsMasterPlayer()
		{
			isMaster = true;
			//if I'm master, I'm now responsible for updating all scene objects
			//FindObjectsOfType<NetworkObject>();
		}

		public static void SendGroupMessage(NetworkObject obj, string group, byte componentIdx, byte[] data, bool reliable = true)
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)VelNetManager.MessageType.ObjectSync);
			writer.Write(obj.networkId);
			writer.Write(componentIdx);
			writer.Write(data.Length);
			writer.Write(data);
			VelNetManager.SendToGroup(group, mem.ToArray(), reliable);
		}

		public static void SendMessage(NetworkObject obj, byte componentIdx, byte[] data, bool reliable = true)
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)VelNetManager.MessageType.ObjectSync);
			writer.Write(obj.networkId);
			writer.Write(componentIdx);
			writer.Write(data.Length);
			writer.Write(data);
			VelNetManager.SendToRoom(mem.ToArray(), false, reliable);
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