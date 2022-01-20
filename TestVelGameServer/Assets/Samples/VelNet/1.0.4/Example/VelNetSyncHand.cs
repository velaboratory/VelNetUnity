using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VelNet;

public class VelNetSyncHand : NetworkSerializedObject
{
    public OVRSkeleton hand;
    public Transform[] toSync;
    public Quaternion[] targets;
    public float smoothness = .1f;
    #region bonesToSync
    /*
    public Transform Hand;
    public Transform Wrist;
    public Transform Index1;
    public Transform Index2;
    public Transform Index3;

    public Transform Middle1;
    public Transform Middle2;
    public Transform Middle3;

    public Transform Ring1;
    public Transform Ring2;
    public Transform Ring3;

    public Transform Pinky0;
    public Transform Pinky1;
    public Transform Pinky2;
    public Transform Pinky3;

    public Transform Thumb0;
    public Transform Thumb1;
    public Transform Thumb2;
    public Transform Thumb3;
    */
    #endregion

    
    private void Start()
    {
        targets = new Quaternion[toSync.Length];
        InvokeRepeating("NetworkSend", 0, 1 / serializationRateHz);
    }

    //TODO: remove when NetworkSerializedObject works
    private void NetworkSend()
    {
        if (IsMine && hand != null && hand.IsDataValid)
        {
            SendBytes(SendState());
        }
    }

    protected override void ReceiveState(byte[] message)
    {
        using MemoryStream mem = new MemoryStream(message);
        using BinaryReader reader = new BinaryReader(mem);
        for (int i =0; i<toSync.Length; i++)
        {
            targets[i] = reader.ReadQuaternion();
        }
    }

    protected override byte[] SendState()
    {
        using MemoryStream mem = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(mem);
        for(int i = 0; i<toSync.Length; i++)
        {
            writer.Write(toSync[0].rotation); //TODO: optimize to just one float for some bones
        }

        return mem.ToArray();
    }

    // Update is called once per frame
    void Update()
    {
        if (IsMine && hand?.Bones != null && hand.IsDataValid) //need to set values from tracked hand to this networkobject
        {
            for(int i = 0; i < toSync.Length; i++)
            {
                toSync[i].rotation = hand.Bones[i].Transform.rotation;
            }
            
            return;
        }

        if(!IsMine) {
            for (int i = 0; i < targets.Length; i++)
            {
                toSync[i].rotation = Quaternion.Slerp(transform.rotation, targets[i], 1 / smoothness / serializationRateHz);
            }
        }
        
    }
}
