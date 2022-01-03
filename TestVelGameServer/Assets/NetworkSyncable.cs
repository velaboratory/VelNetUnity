using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This makes serialization somewhat uniform
/// </summary>
public interface NetworkSyncable 
{
    public byte[] getSyncMessage(); //local owner asks for this and sends it periodically
    public void handleSyncMessage(byte[] message); //remote owner will call this
    
}
