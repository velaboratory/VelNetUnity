#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VelNet.Editor
{
	public class EditorUtils : MonoBehaviour
	{
		[MenuItem("VelNet/Check For Duplicate NetworkIds", false, 10)]
		private static void CheckDuplicateNetworkIds()
		{
			NetworkObject[] objs = FindObjectsOfType<NetworkObject>();
			Dictionary<int, NetworkObject> ids = new Dictionary<int, NetworkObject>();
			foreach (NetworkObject o in objs)
			{
				if (!o.isSceneObject) continue;

				if (ids.ContainsKey(o.sceneNetworkId) || o.sceneNetworkId < 100)
				{
					if (ids.ContainsKey(o.sceneNetworkId))
					{
						Debug.Log($"Found duplicated id: {o.name} {ids[o.sceneNetworkId].name}", o);
					}
					else
					{
						Debug.Log($"Found duplicated id: {o.name} {o.sceneNetworkId}", o);
					}

					o.sceneNetworkId = 100;
					while (ids.ContainsKey(o.sceneNetworkId))
					{
						o.sceneNetworkId += 1;
					}

					PrefabUtility.RecordPrefabInstancePropertyModifications(o);
					EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
				}

				ids.Add(o.sceneNetworkId, o);
			}
		}
	}
}
#endif