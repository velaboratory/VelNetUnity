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
				SerializedObject so = new SerializedObject(o);
				SerializedProperty sceneNetworkId = so.FindProperty("sceneNetworkId");
				if (!o.isSceneObject) continue;

				if (ids.ContainsKey(sceneNetworkId.intValue) || sceneNetworkId.intValue < 100)
				{
					if (ids.ContainsKey(sceneNetworkId.intValue))
					{
						Debug.Log($"Found duplicated id: {o.name} {ids[sceneNetworkId.intValue].name}", o);
					}
					else
					{
						Debug.Log($"Found duplicated id: {o.name} {sceneNetworkId.intValue}", o);
					}

					sceneNetworkId.intValue = 100;
					while (ids.ContainsKey(sceneNetworkId.intValue))
					{
						sceneNetworkId.intValue += 1;
					}

					so.ApplyModifiedProperties();
					EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
				}

				ids.Add(sceneNetworkId.intValue, o);
			}
		}

		[UnityEditor.Callbacks.DidReloadScripts]
		private static void OnScriptsReloaded()
		{
			CheckDuplicateNetworkIds();
		}
	}
}
#endif