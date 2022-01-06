using UnityEngine;

namespace VelNetUnity
{
	/// <summary>
	/// This is a base class for all objects that a player can instantiated/owned
	/// </summary>
	public abstract class NetworkObject : MonoBehaviour
	{
		public NetworkPlayer owner;
		/// <summary>
		/// This is forged from the combination of the creator's id (-1 in the case of a scene object) and an object id, so it's always unique for a room
		/// </summary>
		public string networkId;
		/// <summary>
		/// This may be empty if it's not a prefab (scene object)
		/// </summary>
		public string prefabName;
		public bool isSceneObject;
		public abstract void HandleMessage(string identifier, byte[] message);
	}
}