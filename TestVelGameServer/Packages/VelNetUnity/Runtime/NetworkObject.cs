using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace VelNet
{
	/// <summary>
	/// This is a base class for all objects that a player can instantiated/owned
	/// </summary>
	public class NetworkObject : MonoBehaviour
	{
		[Header("NetworkObject properties")] public VelNetPlayer owner;

		[Tooltip("Whether this object's ownership is transferrable. Should be true for player objects.")]
		public bool ownershipLocked;

		public bool IsMine => owner != null && owner.isLocal;

		/// <summary>
		/// This is forged from the combination of the creator's id (-1 in the case of a scene object) and an object id, so it's always unique for a room
		/// </summary>
		public string networkId;

		/// <summary>
		/// This may be empty if it's not a prefab (scene object)
		/// </summary>
		public string prefabName;

		public bool isSceneObject;

		public List<NetworkComponent> syncedComponents;

		public void SendBytes(NetworkComponent component, byte[] message, bool reliable = true)
		{
			if (!IsMine)
			{
				Debug.LogError("Can't send message if owner is null or not local", this);
				return;
			}

			// send the message and an identifier for which component it belongs to
			int index = syncedComponents.IndexOf(component);
			owner.SendMessage(this, index.ToString(), message, reliable);
		}

		public void SendBytesToGroup(NetworkComponent component, string group, byte[] message, bool reliable = true)
		{
			if (!IsMine)
			{
				Debug.LogError("Can't send message if owner is null or not local", this);
				return;
			}

			// send the message and an identifier for which component it belongs to
			int index = syncedComponents.IndexOf(component);
			owner.SendGroupMessage(this, group, index.ToString(), message, reliable);
		}

		public void ReceiveBytes(string identifier, byte[] message, string str_message = "")
		{
			// send the message to the right component
			var index = int.Parse(identifier);
			if(index < 0 || index >= syncedComponents.Count)
            {
				Debug.LogError("Got message for NetworkComponent that doesn't exist: " + identifier + " on " + prefabName);
				Debug.Log(str_message);
				return;
            }
			syncedComponents[int.Parse(identifier)].ReceiveBytes(message);
		}
	}

#if UNITY_EDITOR
	/// <summary>
	/// Sets up the interface for the CopyTransform script.
	/// </summary>
	[CustomEditor(typeof(NetworkObject))]
	public class NetworkObjectEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			NetworkObject t = target as NetworkObject;

			EditorGUILayout.Space();

			if (t == null) return;

			EditorGUILayout.HelpBox("Network Object. One per prefab pls.\nAssign components to the list to be synced.", MessageType.Info);

			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.Toggle("IsMine", t.IsMine);
			EditorGUILayout.TextField("Owner ID", t.owner?.userid.ToString() ?? "No owner");
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.Space();

			if (GUILayout.Button("Find Network Components and add backreferences."))
			{
				NetworkComponent[] comps = t.GetComponents<NetworkComponent>();
				t.syncedComponents = comps.ToList();
				foreach (NetworkComponent c in comps)
				{
					c.networkObject = t;
				}
			}

			EditorGUILayout.Space();

			DrawDefaultInspector();
		}
	}
#endif
}