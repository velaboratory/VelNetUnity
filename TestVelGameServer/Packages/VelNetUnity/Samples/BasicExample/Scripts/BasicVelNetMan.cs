using UnityEngine;
using VelNet;

public class BasicVelNetMan : MonoBehaviour
{
	public GameObject playerPrefab;

	private void Start()
	{
		VelNetManager.OnLoggedIn += () => VelNetManager.JoinRoom("BasicExample");
		VelNetManager.OnJoinedRoom += _ => { VelNetManager.NetworkInstantiate(playerPrefab.name); };
	}
}