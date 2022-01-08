using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VelNet;

public class VelNetMan : MonoBehaviour
{
	public GameObject playerPrefab;

	// Start is called before the first frame update
	private void Start()
	{
		VelNetManager.instance.OnJoinedRoom += player =>
		{
			VelNetManager.InstantiateNetworkObject(playerPrefab.name);
		};
	}
}