using System.IO;
using UnityEngine.UI;
using VelNet;

public class SyncedTextbox : SyncState
{
	public InputField text;


	protected override void SendState(BinaryWriter binaryWriter)
	{
		binaryWriter.Write(text.text);
	}

	protected override void ReceiveState(BinaryReader binaryReader)
	{
		text.text = binaryReader.ReadString();
	}

	public void TakeOwnership()
	{
		networkObject.TakeOwnership();
	}
}