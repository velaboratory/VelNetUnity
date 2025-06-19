using UnityEngine;
using VelNet;

namespace VelNetExamples
{
	public class GameManager : MonoBehaviour
	{
		public GameObject playerPrefab;

		private void Start()
		{
			VelNetManager.OnLoggedIn += () => VelNetManager.JoinRoom("FullExample");
			VelNetManager.OnJoinedRoom += player => { VelNetManager.NetworkInstantiate(playerPrefab.name); };
		}
	}
}