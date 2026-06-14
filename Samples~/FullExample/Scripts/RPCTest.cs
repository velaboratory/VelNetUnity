using UnityEngine;
using VelNet;

public class RPCTest : NetworkComponent
{
	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.R))
		{
			SendRPC(nameof(TestRPC), true);
		}
	}

	private void TestRPC()
	{
		Debug.Log("RPC RECEIVED!");
	}

	public override void ReceiveBytes(NetworkReader reader)
	{
		Debug.Log("WOW. BYTES");
	}
}
