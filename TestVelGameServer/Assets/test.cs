using UnityEngine;
using VelNet;

public class test : MonoBehaviour
{
	// Start is called before the first frame update
	private void Start()
	{
		VelNetManager.OnJoinedRoom += roomName =>
		{
			Debug.Log("VelNet room joined!!!!!: " + roomName);
			//VelNetManager.GetRoomData(roomName);
			byte[] testpacekt = new[] { (byte)244 };
			VelNetManager.SendCustomMessage(testpacekt, true, true, false);
		};

		VelNetManager.CustomMessageReceived += (senderId, dataWithCategory) =>
		{
			//customPacketReceived(senderId, dataWithCategory);
			if (dataWithCategory[0] == (byte)244)
			{
				Debug.Log("received test packet");
				return;
			}

			;
		};
	}

	// Update is called once per frame
	void Update()
	{
	}
}