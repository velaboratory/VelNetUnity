using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMan : MonoBehaviour
{
    //TODO: communicate tracking space?

    public VelNetSyncHand leftHand;
    public VelNetSyncHand rightHand;

    public Transform head;
    public Transform trackedHead;

    public Transform leftHandAnchor;
    public Transform trackedLeftHandAnchor;

    public Transform rightHandAnchor;
    public Transform trackedRightHandAnchor;

    private void Update()
    {
        if(trackedHead) //must be mine
        {
            head.position = trackedHead.position;
            head.rotation = trackedHead.rotation;
        }

        if(trackedLeftHandAnchor)
        {
            leftHandAnchor.position = trackedLeftHandAnchor.position;
            leftHandAnchor.rotation = trackedLeftHandAnchor.rotation;
        }

        if (trackedRightHandAnchor)
        {
            rightHandAnchor.position = trackedRightHandAnchor.position;
            rightHandAnchor.rotation = trackedRightHandAnchor.rotation;
        }
    }
}
