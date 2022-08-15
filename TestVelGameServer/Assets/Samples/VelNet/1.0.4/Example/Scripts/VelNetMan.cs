using UnityEngine;
using VelNet;

namespace VelNetExample
{
	public class VelNetMan : MonoBehaviour
	{
		public GameObject playerPrefab;

		// Start is called before the first frame update
		private void Start()
		{
			VelNetManager.OnJoinedRoom += player =>
			{
				VelNetManager.NetworkInstantiate(playerPrefab.name);
			};
		}
	}
}