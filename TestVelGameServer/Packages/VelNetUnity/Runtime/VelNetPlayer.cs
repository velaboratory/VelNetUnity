using System.Collections.Generic;
using System;
using System.Text;

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
						VelNetManager.SendToRoom(Encoding.UTF8.GetBytes("7," + kvp.Value.networkId + "," + kvp.Value.prefabName),false,true);
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
		public void HandleMessage(VelNetManager.DataMessage m)
		{
			//for now, we can just convert to text...because 

			string text = Encoding.UTF8.GetString(m.data);

			//types of messages
			string[] messages = text.Split(';'); //messages are split by ;
			foreach (string s in messages)
			{
				//individual message parameters separated by comma
				string[] sections = s.Split(',');

				switch (sections[0])
				{
					case "5": // sync update for an object I may own
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
					case "6": // I'm trying to take ownership of an object 
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

						VelNetManager.NetworkDestroy(networkId);
						break;
					}
					case "9": //deleted scene objects
					{
						for (int k = 1; k < sections.Length; k++)
						{
							VelNetManager.NetworkDestroy(sections[k]);
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
			string message = "5," + obj.networkId + "," + identifier + "," + Convert.ToBase64String(data);
			VelNetManager.SendToGroup(group, Encoding.UTF8.GetBytes(message), reliable);
		}

		public void SendMessage(NetworkObject obj, string identifier, byte[] data, bool reliable = true)
		{
			string message = "5," + obj.networkId + "," + identifier + "," + Convert.ToBase64String(data);
			VelNetManager.SendToRoom(Encoding.UTF8.GetBytes(message), false, reliable);
		}

		public void SendSceneUpdate()
		{
			string message = "9," + string.Join(",", manager.deletedSceneObjects);
			VelNetManager.SendToRoom( Encoding.UTF8.GetBytes(message));
		}

		[Obsolete("Use VelNetManager.NetworkDestroy() instead.")]
		public void NetworkDestroy(string networkId)
		{
			// must be the local owner of the object to destroy it
			if (!manager.objects.ContainsKey(networkId) || manager.objects[networkId].owner != this || !isLocal) return;

			// send to all, which will make me delete as well
			VelNetManager.SendToRoom(Encoding.UTF8.GetBytes("8," + networkId), true, true);
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

			// must be ordered, so that ownership transfers are not confused.  Also sent to all players, so that multiple simultaneous requests will result in the same outcome.
			VelNetManager.SendToRoom(Encoding.UTF8.GetBytes("6," + networkId),true,true);

			return true;
		}
	}
}