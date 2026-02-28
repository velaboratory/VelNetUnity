using UnityEngine.UI;
using VelNet;

public class SyncedTextbox : SyncState
{
	public InputField text;


	protected override void SendState(NetworkWriter writer)
	{
		writer.Write(text.text);
	}

	protected override void ReceiveState(NetworkReader reader)
	{
		text.text = reader.ReadString();
	}

	public void TakeOwnership()
	{
		networkObject.TakeOwnership();
	}
}
