using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VelNet
{
	public class UndoGroup : MonoBehaviour
	{
		/// <summary>
		/// This cannot be changed at runtime.
		/// </summary>
		public List<SyncState> objects;

		private readonly List<byte[][]> undoBuffer = new List<byte[][]>();
		public int maxUndoSteps = 50;
		public bool debugLog;


		/// <summary>
		/// Reset to the last UndoState. This only takes ownership if the IPackState component is also a NetworkComponent
		/// </summary>
		public void Undo()
		{
			byte[][] lastStates = undoBuffer.LastOrDefault();
			if (lastStates != null)
			{
				for (int i = 0; i < objects.Count; i++)
				{
					objects[i].networkObject.TakeOwnership();
					objects[i].UnpackState(lastStates[i]);
				}
				if (debugLog) Debug.Log($"Undo {objects.Count} objects");
			}
			else
			{
				if (debugLog) Debug.Log($"No more undo to undo");
			}
		}

		public void SaveUndoState()
		{
			byte[][] states = new byte[objects.Count][];
			for (int i = 0; i < objects.Count; i++)
			{
				states[i] = objects[i].PackState();
			}

			undoBuffer.Add(states);

			if (debugLog) Debug.Log($"Saved undo state");
			while (undoBuffer.Count > maxUndoSteps)
			{
				undoBuffer.RemoveAt(0);
				if (debugLog) Debug.Log($"Reached maximum undo history");
			}
			
		}

		public int UndoHistoryLength()
		{
			return undoBuffer.Count;
		}
	}
	
#if UNITY_EDITOR
	[CustomEditor(typeof(UndoGroup))]
	public class UndoGroupEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			UndoGroup t = target as UndoGroup;

			EditorGUILayout.Space();

			if (t == null) return;

			EditorGUILayout.HelpBox("Undo Group. Use SaveUndoState() to make checkpoints.", MessageType.Info);

			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.TextField("Undo Length: ", t.UndoHistoryLength().ToString("N0"));
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.Space();

			if (EditorApplication.isPlaying && GUILayout.Button("Save undo checkpoint now."))
			{
				t.SaveUndoState();
			}

			if (EditorApplication.isPlaying && GUILayout.Button("Undo now"))
			{
				t.Undo();
			}

			if (GUILayout.Button("Find all undoable components in children."))
			{
				SyncState[] components = t.GetComponentsInChildren<SyncState>();
				SerializedObject so = new SerializedObject(t);
				SerializedProperty prop = so.FindProperty("objects");
				prop.ClearArray();
				foreach (SyncState comp in components)
				{
					prop.InsertArrayElementAtIndex(0);
					prop.GetArrayElementAtIndex(0).objectReferenceValue = comp;
				}

				so.ApplyModifiedProperties();
			}

			EditorGUILayout.Space();

			DrawDefaultInspector();
		}
	}
#endif
}