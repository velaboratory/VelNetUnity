using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System;
using Dissonance;

public class NetworkPlayer : MonoBehaviour, Dissonance.IDissonancePlayer
{
    public int userid;
    public string username;

    public string room;
    public NetworkManager manager;
    Vector3 networkPosition;
    public bool isLocal = false;
    
    public Dissonance.VelCommsNetwork commsNetwork;
    bool isSpeaking = false;
    uint lastAudioId = 0;
    public string dissonanceID;
    //required by dissonance for spatial audio
    public string PlayerId => dissonanceID;
    public Vector3 Position => transform.position;
    public Quaternion Rotation => transform.rotation;
    public NetworkPlayerType Type => isLocal?NetworkPlayerType.Local:NetworkPlayerType.Remote;
    public bool IsTracking => true;

    void Start()
    {
        if (manager.userid == userid) //this is me, I send updates
        {
            StartCoroutine(syncTransformCoroutine());
        }
    }

    // Update is called once per frame
    void Update()
    {

        if (!isLocal) //not me, I move to wherever I should
        {
            transform.position = Vector3.Lerp(transform.position, networkPosition, .1f);
            
        }
        else
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 delta = h * Vector3.right + v * Vector3.up;
            transform.position = transform.position + delta * Time.deltaTime;

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

    IEnumerator syncTransformCoroutine()
    {
        while (true)
        {
            manager.sendTo(0, "1," + transform.position.x + "," + transform.position.y + "," + transform.position.z + ";");
            yield return new WaitForSeconds(.1f);
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
                case "1": //update transform of self
                    {
                        float x = float.Parse(sections[1]);
                        float y = float.Parse(sections[2]);
                        float z = float.Parse(sections[3]);
                        networkPosition = new Vector3(x, y, z);
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
}
