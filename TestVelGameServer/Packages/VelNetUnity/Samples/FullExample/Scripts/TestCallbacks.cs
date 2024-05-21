using UnityEngine;
using VelNet;

public class TestCallbacks : MonoBehaviour
{
	private void Start()
	{
		VelNetManager.OnJoinedRoom += _ => { Debug.Log("VelNetManager.OnJoinedRoom"); };
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
}