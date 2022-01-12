using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VelNet;

public class VelNetMan : MonoBehaviour
{
	public GameObject playerPrefab;
	public bool startAutomagically;
	public OVRSkeleton leftHand;
	public OVRSkeleton rightHand;
	public Transform hmd;
	public Transform leftAnchor;
	public Transform rightAnchor;

	// Start is called before the first frame update
	private void Start()
	{
		VelNetManager.OnJoinedRoom += room =>
		{
			GameObject created = VelNetManager.InstantiateNetworkObject(playerPrefab.name);
			PlayerMan playerMan = created.GetComponent<PlayerMan>();
            if (playerMan) {
				playerMan.leftHand.hand = leftHand;
				playerMan.rightHand.hand = rightHand;
				playerMan.trackedHead = hmd;
				playerMan.trackedLeftHandAnchor = leftAnchor;
				playerMan.trackedRightHandAnchor = rightAnchor;
			}
		};

		if(startAutomagically)
        {
			VelNetManager.OnConnectedToServer += () =>
			{
				VelNetManager.Login(SystemInfo.deviceUniqueIdentifier, "nopass");
				VelNetManager.Join("0");
			};
		}
	}
}