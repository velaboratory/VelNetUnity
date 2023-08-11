using System.IO;
using UnityEngine;
using VelNet;
using Random = UnityEngine.Random;

public class UndoableObjectTest : SyncState
{
	public UndoGroup undoGroup;
	private Renderer rend;

	private void Start()
	{
		rend = GetComponent<Renderer>();
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.Y))
		{
			undoGroup.SaveUndoState();
			Debug.Log($"Saved Undo state. There are {undoGroup.UndoHistoryLength():N0} undo states.");
		}

		if (Input.GetKeyDown(KeyCode.U))
		{
			undoGroup.Undo();
			Debug.Log($"Undo. There are {undoGroup.UndoHistoryLength():N0} undo states.");
		}

		if (Input.GetKeyDown(KeyCode.C))
		{
			Debug.Log("Changing color");
			networkObject.TakeOwnership();
			rend.material.color = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f));
		}
	}

	protected override void SendState(BinaryWriter binaryWriter)
	{
		binaryWriter.Write(rend.material.color);
	}

	protected override void ReceiveState(BinaryReader binaryReader)
	{
		rend.material.color = binaryReader.ReadColor();
	}
}