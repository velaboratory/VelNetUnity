using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VelNet;

public class VelNetSyncHand : NetworkSerializedObject
{
    public OVRSkeleton hand;
    public OVRCustomSkeleton toSync;
    public Quaternion[] targets;
    public float smoothness = .1f;
    protected override void ReceiveState(byte[] message)
    {
        using MemoryStream mem = new MemoryStream(message);
        using BinaryReader reader = new BinaryReader(mem);
        for (int i =0; i<toSync.Bones.Count; i++)
        {
            targets[i] = reader.ReadQuaternion();
        }
    }

    protected override byte[] SendState()
    {
        using MemoryStream mem = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(mem);
        for(int i = 0; i<toSync.Bones.Count; i++)
        {
            writer.Write(toSync.Bones[0].Transform.rotation); //TODO: optimize to just one float for some bones
        }

        return mem.ToArray();
    }

    // Start is called before the first frame update
    void Start()
    {
        targets = new Quaternion[toSync.Bones.Count];
    }

    // Update is called once per frame
    void Update()
    {
        if (IsMine && hand) //need to set values from tracked hand to this networkobject
        {
            for(int i = 0; i < hand.Bones.Count; i++)
            {
                toSync.Bones[i].Transform.rotation = hand.Bones[i].Transform.rotation;
            }
            
            return;
        }
        for(int i = 0;i<targets.Length;i++)
        {
            toSync.Bones[i].Transform.rotation = Quaternion.Slerp(transform.rotation, targets[i], 1 / smoothness / serializationRateHz);
        }
    }
}
