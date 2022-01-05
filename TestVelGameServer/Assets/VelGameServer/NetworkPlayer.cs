using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System;


[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayer : MonoBehaviour
{
    
    public NetworkObject myObject; 
    public int userid;
    public string username;

    public string room;
    public NetworkManager manager;

    public bool isLocal = false;

    public int lastObjectId=0; //for instantiation

    
    bool isMaster = false;

    void Start()
    {
        myObject.owner = this;
        this.manager = GameObject.FindObjectOfType<NetworkManager>();
        manager.onPlayerJoined += handlePlayerJoined;
    }

    // Update is called once per frame
    void Update()
    {
        

       
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

            if (isMaster)
            {
                //send a list of scene object ids when someone joins
                sendSceneUpdate();
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
                        string identifier = sections[1];
                        byte[] message = Convert.FromBase64String(sections[2]);
                        myObject.handleMessage(identifier, message);
                        break;
                    }
                case "5": //sync update for an object I may own
                    {

                        string objectKey = sections[1];
                        string identifier = sections[2];
                        string syncMessage = sections[3];
                        byte[] messageBytes = Convert.FromBase64String(syncMessage);
                        if (manager.objects.ContainsKey(objectKey))
                        {
                            if(manager.objects[objectKey].owner == this)
                            {
                                manager.objects[objectKey].handleMessage(identifier, messageBytes);
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
                case "8": //I'm trying to destroy a gameobject I own
                    {
                        string networkId = sections[1];

                        manager.deleteNetworkObject(networkId);
                        break;
                    }
                case "9": //deleted scene objects
                    {
                        
                        for (int k = 1; k < sections.Length; k++)
                        {
                            manager.deleteNetworkObject(sections[k]);
                        }
                            
                        break;
                    }
            }
        }

    }

    public void setAsMasterPlayer()
    {
        isMaster = true;
        //if I'm master, I'm now responsible for updating all scene objects
        //FindObjectsOfType<NetworkObject>();
    }
    
    public void sendGroupMessage(NetworkObject obj, string group, string identifier, byte[] data, bool reliable=true)
    {
        if (obj == myObject)
        {
            manager.sendToGroup(group, "1," + identifier + "," + Convert.ToBase64String(data), reliable);
        }
        else
        {
            manager.sendToGroup(group, "5," + obj.networkId + "," + identifier + "," + Convert.ToBase64String(data), reliable);
        }
    }
    public void sendMessage(NetworkObject obj, string identifier, byte[] data, bool reliable=true)
    {
        if (obj == myObject)
        {
            manager.sendTo(NetworkManager.MessageType.OTHERS, "1," + identifier +"," + Convert.ToBase64String(data), reliable);
        }
        else
        {

            manager.sendTo(NetworkManager.MessageType.OTHERS, "5," + obj.networkId + "," + identifier + "," + Convert.ToBase64String(data), reliable);
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

    public void sendSceneUpdate()
    {
       
        manager.sendTo(NetworkManager.MessageType.OTHERS, "9," + string.Join(",", manager.deletedSceneObjects));
    }

    
}
