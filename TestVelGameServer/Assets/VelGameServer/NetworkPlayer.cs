using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System;
using Dissonance;

[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayer : MonoBehaviour, Dissonance.IDissonancePlayer
{
    
    public NetworkObject myObject; 
    public int userid;
    public string username;

    public string room;
    public NetworkManager manager;

    public bool isLocal = false;

    public int lastObjectId=0; //for instantiation

    public Dissonance.VelCommsNetwork commsNetwork;
    bool isSpeaking = false;
    uint lastAudioId = 0;
    public string dissonanceID;
    //required by dissonance for spatial audio
    public string PlayerId => dissonanceID;
    public Vector3 Position => myObject.transform.position;
    public Quaternion Rotation => myObject.transform.rotation;
    public NetworkPlayerType Type => isLocal?NetworkPlayerType.Local:NetworkPlayerType.Remote;
    public bool IsTracking => true;
    bool isMaster = false;

    public List<int> closePlayers = new List<int>(); //for testing audio communications
    public bool changeClosePlayers = true;
    void Start()
    {
        myObject.owner = this;
        this.manager = GameObject.FindObjectOfType<NetworkManager>();
        manager.onPlayerJoined += handlePlayerJoined;
    }

    // Update is called once per frame
    void Update()
    {
        //handle dissonance comms
        if(isLocal)
        {
            
            //if we're not speaking, and the comms say we are, send a speaking event, which will be received on other network players and sent to their comms accordingly
            if (commsNetwork.comms.FindPlayer(dissonanceID).IsSpeaking != isSpeaking) //unfortunately, there does not seem to be an event for this
            {
                isSpeaking = !isSpeaking;
                manager.sendTo(NetworkManager.MessageType.OTHERS, "4," + (isSpeaking?1:0) + ";");
                if (!isSpeaking)
                {
                    lastAudioId = 0;
                }
                
            }
            
        }



    }

    public void handlePlayerJoined(NetworkPlayer player)
    {
        //if this is the local player, go through the objects that I own, and send instantiation messages for the ones that have prefab names
        if (this.isLocal)
        {
            foreach(KeyValuePair<string,NetworkObject> kvp in manager.objects)
            {
                if(kvp.Value.owner == this && kvp.Value.prefabName != "")
                {
                    manager.sendTo(NetworkManager.MessageType.OTHERS, "7," + kvp.Value.networkId + "," + kvp.Value.prefabName);
                    
                }
            }
        }
    }
    

    public void handleMessage(NetworkManager.Message m)
    {
        //these are generally things that come from the "owner" and should be enacted locally, where appropriate
        //we need to parse the message

        //types of messages
        string[] messages = m.text.Split(';'); //messages are split by ;
        for(int i = 0; i < messages.Length; i++)
        {
            //individual message parameters separated by comma
            string[] sections = messages[i].Split(',');

            switch (sections[0])
            {
                case "1": //update my object's data
                    {
                        byte[] message = Convert.FromBase64String(sections[1]);
                        myObject.handleSyncMessage(message);
                        break;
                    }
                case "2": //audio data
                    {

                        if (isSpeaking)
                        {
                            byte[] data = Convert.FromBase64String(sections[1]);
                            uint sequenceNumber = uint.Parse(sections[2]);
                            commsNetwork.voiceReceived(dissonanceID, data, sequenceNumber);
                        }
                        
                        break;
                    }
                case "3": //dissonance id (player joined)
                    {
                        if (dissonanceID == "")
                        {
                            dissonanceID = sections[1];
                            //tell the comms network that this player joined the channel
                            commsNetwork.playerJoined(dissonanceID); //tell dissonance
                            commsNetwork.comms.TrackPlayerPosition(this); //tell dissonance to track the remote player
                        }
                        break;
                    }
                case "4": //speaking state
                    {
                        if(sections[1] == "0")
                        {
                            commsNetwork.playerStoppedSpeaking(dissonanceID);
                            isSpeaking = false;
                        }
                        else
                        {
                            commsNetwork.playerStartedSpeaking(dissonanceID);
                            isSpeaking = true;
                        }
                        break;
                    }
                case "5": //sync update for an object I may own
                    {

                        string objectKey = sections[1];
                        string syncMessage = sections[2];
                        byte[] messageBytes = Convert.FromBase64String(syncMessage);
                        if (manager.objects.ContainsKey(objectKey))
                        {
                            if(manager.objects[objectKey].owner == this)
                            {
                                manager.objects[objectKey].handleSyncMessage(messageBytes);
                            }
                        }

                        break;
                    }
                case "6": //I'm trying to take ownership of an object 
                    {
                        string networkId = sections[1];
                        
                        if (manager.objects.ContainsKey(networkId))
                        {
                            manager.objects[networkId].owner = this;
                        }
                        
                        break;
                    }
                case "7": //I'm trying to instantiate an object 
                    {
                        string networkId = sections[1];
                        string prefabName = sections[2];
                        if (manager.objects.ContainsKey(networkId))
                        {
                            break; //we already have this one, ignore
                        }
                        NetworkObject temp = manager.prefabs.Find((prefab) => prefab.name == prefabName);
                        if (temp != null)
                        {
                            NetworkObject instance = GameObject.Instantiate<NetworkObject>(temp);
                            instance.networkId = networkId;
                            instance.prefabName = prefabName;
                            instance.owner = this;
                            manager.objects.Add(instance.networkId, instance);
                        }

                        break;
                    }
                case "8": //I'm trying to destroy a gameobject I own (I guess this is sent to everyone)
                    {
                        string networkId = sections[1];

                        if (manager.objects.ContainsKey(networkId) && manager.objects[networkId].owner == this)
                        { 
                            GameObject.Destroy(manager.objects[networkId].gameObject);
                            manager.objects.Remove(networkId);
                        }
                        break;
                    }
            }
        }

    }

    public void OnDestroy()
    {
        commsNetwork.playerLeft(dissonanceID);
    }

    public void sendAudioData(ArraySegment<byte> data)
    {
        if (changeClosePlayers)
        {
            manager.setupMessageGroup("close", closePlayers.ToArray());
            changeClosePlayers = false;
        }
        string b64_data = Convert.ToBase64String(data.Array,data.Offset,data.Count);
        //manager.sendTo(NetworkManager.MessageType.OTHERS, "2," + b64_data + "," + (lastAudioId++) + ";");
        manager.sendToGroup("close", "2,"+b64_data + ","+ (lastAudioId++) +";");
    }

    public void setDissonanceID(string id) //this sort of all initializes dissonance
    {
        dissonanceID = id;
        Debug.Log("here");
        manager.sendTo(NetworkManager.MessageType.OTHERS, "3," + id+";");
        commsNetwork.comms.TrackPlayerPosition(this);
    }

    public void setAsMasterPlayer()
    {
        isMaster = true;
        //if I'm master, I'm now responsible for updating all scene objects
        //FindObjectsOfType<NetworkObject>();
    }
    public void syncObject(NetworkObject obj)
    {
        byte[] data = obj.getSyncMessage();
        if (obj == myObject)
        {
            manager.sendTo(NetworkManager.MessageType.OTHERS, "1," + Convert.ToBase64String(data));
        }
        else
        {
            
            manager.sendTo(NetworkManager.MessageType.OTHERS, "5," + obj.networkId + "," + Convert.ToBase64String(data));
        }
    }

    public NetworkObject networkInstantiate(string prefabName)
    {
        if (!isLocal)
        {
            return null; //must be the local player to call instantiate
        }
        string networkId = this.userid + "-" + lastObjectId++;
        

        NetworkObject temp = manager.prefabs.Find((prefab) => prefab.name == prefabName);
        if (temp != null)
        {
            NetworkObject instance = GameObject.Instantiate<NetworkObject>(temp);
            instance.networkId = networkId;
            instance.prefabName = prefabName;
            instance.owner = this;
            manager.objects.Add(instance.networkId, instance);

            manager.sendTo(NetworkManager.MessageType.OTHERS, "7," + networkId + "," + prefabName); //only sent to others, as I already instantiated this.  Nice that it happens immediately. 
            return instance;
        }
        return null;
    }

    public void networkDestroy(string networkId)
    {
        if (!manager.objects.ContainsKey(networkId) || manager.objects[networkId].owner != this || !isLocal) return; //must be the local owner of the object to destroy it
        manager.sendTo(NetworkManager.MessageType.ALL_ORDERED, "8," + networkId); //send to all, which will make me delete as well
    }

    public void takeOwnership(string networkId)
    {
        if (!manager.objects.ContainsKey(networkId) || !isLocal) return; //must exist and be the the local player

        manager.objects[networkId].owner = this; //immediately successful
        manager.sendTo(NetworkManager.MessageType.ALL_ORDERED, "6," + networkId); //must be ordered, so that ownership transfers are not confused.  Also sent to all players, so that multiple simultaneous requests will result in the same outcome.

    }

    
}
