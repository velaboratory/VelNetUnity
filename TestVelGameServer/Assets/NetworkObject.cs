using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This is a base class for all objects that a player can instantiated/owned
/// </summary>
public abstract class NetworkObject: MonoBehaviour, NetworkSyncable
{
    public NetworkPlayer owner;
    public string networkId; //this is forged from the combination of the creator's id (-1 in the case of a scene object) and an object id, so it's always unique for a room

    public abstract byte[] getSyncMessage();
    public abstract void handleSyncMessage(byte[] message);
}
