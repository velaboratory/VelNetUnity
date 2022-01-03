using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class NetworkObject: MonoBehaviour
{
    public NetworkPlayer owner;
    public string networkId;
    public abstract byte[] getSyncMessage(); //local owner asks for this and sends it periodically
    public abstract void handleSyncMessage(byte[] message); //remote owner will call this
}
