using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

/// <summary>
/// A simple class that will sync the position and rotation of a network object
/// </summary>
public class SyncTransform : NetworkObject
{
    
    public Vector3 targetPosition;
    public Quaternion targetRotation;


    public override byte[] getSyncMessage()
    {
        float[] data = new float[7];
        for(int i = 0; i < 3; i++)
        {
            data[i] = transform.position[i];
            data[i + 3] = transform.rotation[i];
        }
        data[6] = transform.rotation[3];

        byte[] toReturn = new byte[sizeof(float) * data.Length];
        Buffer.BlockCopy(data, 0, toReturn,0, toReturn.Length);
        return toReturn;
    }

    public override void handleSyncMessage(byte[] message)
    {
        float[] data = new float[7];
        Buffer.BlockCopy(message, 0, data, 0, message.Length);
        for(int i = 0; i < 3; i++)
        {
            targetPosition[i] = data[i];
            targetRotation[i] = data[i + 3];
        }
        targetRotation[3] = data[6];
    }

    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(syncBehavior());
    }

    IEnumerator syncBehavior()
    {
        while (true)
        {
            if (owner != null && owner.isLocal)
            {
                
                owner.syncObject(this);
            }
            yield return new WaitForSeconds(.1f);
        }
    }
    // Update is called once per frame
    void Update()
    {
        if (owner != null && !owner.isLocal)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, .1f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, .1f);
        }
    }

}
