using System.Collections.Generic;
using System;

namespace VelNet
{
	/// <summary>
	/// Represents a network player
	/// </summary>
	public class NetworkPlayer
	{
		public int userid;
		public string username;

		public string room;

		public bool isLocal;

		private VelNetManager manager;

		/// <summary>
		/// For instantiation
		/// </summary>
		public int lastObjectId;


		private bool isMaster;


		public NetworkPlayer()
		{
			manager = VelNetManager.instance;
			manager.OnPlayerJoined += HandlePlayerJoined;
		}

		public void HandlePlayerJoined(NetworkPlayer player)
		{
			//if this is the local player, go through the objects that I own, and send instantiation messages for the ones that have prefab names
			if (isLocal)
			{
				foreach (KeyValuePair<string, NetworkObject> kvp in manager.objects)
				{
					if (kvp.Value.owner == this && kvp.Value.prefabName != "")
					{
						manager.SendTo(VelNetManager.MessageType.OTHERS, "7," + kvp.Value.networkId + "," + kvp.Value.prefabName);
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
		/// </summary>
		public void HandleMessage(VelNetManager.Message m)
		{
			//we need to parse the message

			//types of messages
			string[] messages = m.text.Split(';'); //messages are split by ;
			foreach (string s in messages)
			{
				//individual message parameters separated by comma
				string[] sections = s.Split(',');

				switch (sections[0])
				{
					case "5": //sync update for an object I may own
					{
						string objectKey = sections[1];
						string identifier = sections[2];
						string syncMessage = sections[3];
						byte[] messageBytes = Convert.FromBase64String(syncMessage);
						if (manager.objects.ContainsKey(objectKey))
						{
							if (manager.objects[objectKey].owner == this)
							{
								manager.objects[objectKey].ReceiveBytes(identifier, messageBytes);
							}
						}

						break;
					}
					case "6": //I'm trying to take ownership of an object 
					{
						string networkId = sections[1];

						if (manager.objects.ContainsKey(networkId))
						{
							manager.objects[networkId].owner = this;
						}

						break;
					}
					case "7": // I'm trying to instantiate an object 
					{
						string networkId = sections[1];
						string prefabName = sections[2];
						if (manager.objects.ContainsKey(networkId))
						{
							break; //we already have this one, ignore
						}

						VelNetManager.SomebodyInstantiatedNetworkObject(networkId, prefabName, this);

						break;
					}
					case "8": // I'm trying to destroy a gameobject I own
					{
						string networkId = sections[1];

						manager.DeleteNetworkObject(networkId);
						break;
					}
					case "9": //deleted scene objects
					{
						for (int k = 1; k < sections.Length; k++)
						{
							manager.DeleteNetworkObject(sections[k]);
						}

						break;
					}
				}
			}
		}

		public void SetAsMasterPlayer()
		{
			isMaster = true;
			//if I'm master, I'm now responsible for updating all scene objects
			//FindObjectsOfType<NetworkObject>();
		}

		public void SendGroupMessage(NetworkObject obj, string group, string identifier, byte[] data, bool reliable = true)
		{
			manager.SendToGroup(group, "5," + obj.networkId + "," + identifier + "," + Convert.ToBase64String(data), reliable);
		}

		public void SendMessage(NetworkObject obj, string identifier, byte[] data, bool reliable = true)
		{
			manager.SendTo(VelNetManager.MessageType.OTHERS, "5," + obj.networkId + "," + identifier + "," + Convert.ToBase64String(data), reliable);
		}

		/// <summary>
		/// TODO could move this to a static method in VelNetManager
		/// </summary>
		public void NetworkDestroy(string networkId)
		{
			// must be the local owner of the object to destroy it
			if (!manager.objects.ContainsKey(networkId) || manager.objects[networkId].owner != this || !isLocal) return;

			// send to all, which will make me delete as well
			manager.SendTo(VelNetManager.MessageType.ALL_ORDERED, "8," + networkId);
		}

		/// <summary>
		/// TODO could move this to a static method in VelNetManager
		/// </summary>
		/// <returns>True if successful, False if failed to transfer ownership</returns>
		public bool TakeOwnership(string networkId)
		{
			// must exist and be the the local player
			if (!manager.objects.ContainsKey(networkId) || !isLocal) return false;

			// if the ownership is locked, fail
			if (manager.objects[networkId].ownershipLocked) return false;
			
			// immediately successful
			manager.objects[networkId].owner = this;

			// must be ordered, so that ownership transfers are not confused.  Also sent to all players, so that multiple simultaneous requests will result in the same outcome.
			manager.SendTo(VelNetManager.MessageType.ALL_ORDERED, "6," + networkId);
			
			return true;
		}

		public void SendSceneUpdate()
		{
			manager.SendTo(VelNetManager.MessageType.OTHERS, "9," + string.Join(",", manager.deletedSceneObjects));
		}
	}
}