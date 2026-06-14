using UnityEngine;
using VelNet;

public class CustomMessageTest : MonoBehaviour
{
	private void Start()
	{
		VelNetManager.OnJoinedRoom += _ =>
		{
			byte[] testPacket = { 244 };
			VelNetManager.SendCustomMessage(testPacket, true, true, false);
		};

		VelNetManager.CustomMessageReceived += (senderId, dataWithCategory) =>
		{
			if (dataWithCategory[0] == 244)
			{
				Debug.Log($"Received test packet from {senderId}");
			}
		};
	}
}