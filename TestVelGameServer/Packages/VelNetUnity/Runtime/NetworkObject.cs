using System;
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
		/// This is generated at editor time and used to generated the network id at runtime.
		/// This is needed because finding all objects of type at runtime doesn't have a guaranteed order.
		/// </summary>
		public int sceneNetworkId;

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
			if (!syncedComponents.Contains(component))
			{
				Debug.LogError("Can't send message if this component is not registered with the NetworkObject.", this);
				return;
			}

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
			try
			{
				syncedComponents[int.Parse(identifier)].ReceiveBytes(message);
			}
			catch (Exception e)
			{
				Debug.LogError($"Error in handling message:\n{e}", this);
			}
		}

		public void TakeOwnership()
		{
			VelNetManager.TakeOwnership(networkId);
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

			// make the sceneNetworkId a new unique value
			if (Application.isEditor && !Application.isPlaying && t.isSceneObject && t.sceneNetworkId == 0)
			{
				// find the first unused value
				int[] used = FindObjectsOfType<NetworkObject>().Select(o => o.sceneNetworkId).ToArray();
				int available = -1;
				for (int i = 1; i <= used.Max()+1; i++)
				{
					if (!used.Contains(i))
					{
						available = i;
						break;
					}
				}

				t.sceneNetworkId = available;
				PrefabUtility.RecordPrefabInstancePropertyModifications(t);
			}

			EditorGUILayout.Space();

			DrawDefaultInspector();
		}
	}
#endif
}
