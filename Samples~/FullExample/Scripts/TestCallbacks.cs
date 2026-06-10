using UnityEngine;
using VelNet;

namespace VelNetExamples
{
	public class TestCallbacks : MonoBehaviour
	{
		public int ping;

		private void Start()
		{
			VelNetManager.OnJoinedRoom += roomId =>
			{
				Debug.Log("VelNetManager.OnJoinedRoom");
				VelNetManager.GetRoomData(roomId);
			};
			VelNetManager.OnLeftRoom += _ => { Debug.Log("VelNetManager.OnLeftRoom"); };
			VelNetManager.OnPlayerJoined += (_, _) => { Debug.Log("VelNetManager.OnPlayerJoined"); };
			VelNetManager.OnPlayerLeft += _ => { Debug.Log("VelNetManager.OnPlayerLeft"); };
			VelNetManager.OnConnectedToServer += () => { Debug.Log("VelNetManager.OnConnectedToServer"); };
			VelNetManager.OnDisconnectedFromServer += () => { Debug.Log("VelNetManager.OnDisconnectedFromServer"); };
			VelNetManager.OnFailedToConnectToServer += () => { Debug.Log("VelNetManager.OnFailedToConnectToServer"); };
			VelNetManager.OnLoggedIn += () => { Debug.Log("VelNetManager.OnLoggedIn"); };
			VelNetManager.RoomsReceived += _ => { Debug.Log("VelNetManager.RoomsReceived"); };
			VelNetManager.RoomDataReceived += _ => { Debug.Log("VelNetManager.RoomDataReceived"); };
			VelNetManager.MessageReceived += _ => { Debug.Log("VelNetManager.MessageReceived"); };
			VelNetManager.CustomMessageReceived += (_, _) => { Debug.Log("VelNetManager.CustomMessageReceived"); };
			VelNetManager.OnLocalNetworkObjectSpawned += _ => { Debug.Log("VelNetManager.OnLocalNetworkObjectSpawned"); };
			VelNetManager.OnNetworkObjectSpawned += _ => { Debug.Log("VelNetManager.OnNetworkObjectSpawned"); };
			VelNetManager.OnNetworkObjectDestroyed += _ => { Debug.Log("VelNetManager.OnNetworkObjectDestroyed"); };
		}

		private void Update()
		{
			ping = VelNetManager.Ping;
		}
	}
}