using UnityEngine;
using VelNet;

namespace VelNet
{
	public class BasicVelNetMan : MonoBehaviour
	{
		public GameObject playerPrefab;

		private void Start()
		{
			// join a hardcoded VelNet room as soon as we log into the server
			VelNetManager.OnLoggedIn += () => VelNetManager.JoinRoom("BasicExample");
			// then once we join the room, spawn our player prefab on the network
			VelNetManager.OnJoinedRoom += _ => { VelNetManager.NetworkInstantiate(playerPrefab.name); };
		}
	}
}