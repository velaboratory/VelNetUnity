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

    void Awake()
    {
        myObject.owner = this;
    }

    // Update is called once per frame
    void Update()
    {
        //handle dissonance comms
        if(isLocal)
        {
            //if we're not speaking, and the comms say we are, send a speaking event, which will be received on other network players and sent to their comms accordingly
            if(commsNetwork.comms.FindPlayer(dissonanceID).IsSpeaking != isSpeaking) //unfortunately, there does not seem to be an event for this
            {
                isSpeaking = !isSpeaking;
                manager.sendTo(0, "4," + (isSpeaking?1:0) + ";");
                if (!isSpeaking)
                {
                    lastAudioId = 0;
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
                        int objectId = int.Parse(sections[1]);
                        int creatorId = int.Parse(sections[2]);
                        string objectKey = creatorId + "-" + objectId;
                        if (manager.objects.ContainsKey(objectKey))
                        {
                            manager.objects[objectKey].owner = this;
                            
                        }
                        
                        break;
                    }
                case "7": //I'm trying to instantiate an object (sent to everyone)
                    {
                        int objectId = int.Parse(sections[1]);
                        string prefabName = sections[2];
                        NetworkObject temp = manager.prefabs.Find((prefab) => prefab.name == prefabName);
                        if (temp != null)
                        {
                            NetworkObject instance = GameObject.Instantiate<NetworkObject>(temp);
                            instance.networkId = this.userid + "-" + objectId;
                            
                            instance.owner = this;
                            manager.objects.Add(instance.networkId, instance);
                        }

                        break;
                    }
                case "8": //I'm trying to destroy a gameobject I own (I guess this is sent to everyone)
                    {
                        int objectId = int.Parse(sections[1]);
                        int creatorId = int.Parse(sections[2]);
                        string objectKey = creatorId + "-" + objectId;
                        if (manager.objects.ContainsKey(objectKey))
                        {
                            if (manager.objects[objectKey].owner == this)
                            {
                                GameObject.Destroy(manager.objects[objectKey].gameObject);
                            }
                            manager.objects.Remove(objectKey);
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
        string b64_data = Convert.ToBase64String(data.Array,data.Offset,data.Count);
        manager.sendTo(0, "2,"+b64_data + ","+ (lastAudioId++) +";");
    }

    public void setDissonanceID(string id) //this sort of all initializes dissonance
    {
        dissonanceID = id;
        manager.sendTo(0, "3," + id+";");
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
            manager.sendTo(0, "1," + Convert.ToBase64String(data));
        }
        else
        {
            
            manager.sendTo(0, "5," + obj.networkId + "," + Convert.ToBase64String(data));
        }
    }

    public void instantiateObject(string prefab)
    {

    }

    
}
