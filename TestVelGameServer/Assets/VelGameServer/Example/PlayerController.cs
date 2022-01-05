using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Dissonance;
using System.Text;
public class PlayerController : NetworkObject, Dissonance.IDissonancePlayer
{
    VelCommsNetwork comms;
    bool isSpeaking = false;
    uint lastAudioId = 0;
    public string dissonanceID="";
    //required by dissonance for spatial audio
    public string PlayerId => dissonanceID;
    public Vector3 Position => transform.position;
    public Quaternion Rotation => transform.rotation;
    public NetworkPlayerType Type => this.owner.isLocal ? NetworkPlayerType.Local : NetworkPlayerType.Remote;
    public bool IsTracking => true;

    public Vector3 targetPosition;
    public Quaternion targetRotation;

    public List<int> closePlayers = new List<int>();


    public byte[] getSyncMessage()
    {
        float[] data = new float[7];
        for (int i = 0; i < 3; i++)
        {
            data[i] = transform.position[i];
            data[i + 3] = transform.rotation[i];
        }
        data[6] = transform.rotation[3];

        byte[] toReturn = new byte[sizeof(float) * data.Length];
        Buffer.BlockCopy(data, 0, toReturn, 0, toReturn.Length);
        return toReturn;
    }

    public override void handleMessage(string identifier, byte[] message)
    {
        switch (identifier)
        {
            case "s": //sync message
                float[] data = new float[7];
                Buffer.BlockCopy(message, 0, data, 0, message.Length);
                for (int i = 0; i < 3; i++)
                {
                    targetPosition[i] = data[i];
                    targetRotation[i] = data[i + 3];
                }
                targetRotation[3] = data[6];
                break;
            case "a": //audio data
                {
                    if (isSpeaking)
                    {
                        comms.voiceReceived(dissonanceID, message);
                    }
                    break;
                }
            case "d": //dissonance id (player joined)
                {
                    if (dissonanceID == "") //I don't have this yet
                    {
                        dissonanceID = Encoding.UTF8.GetString(message);
                        //tell the comms network that this player joined the channel
                        comms.playerJoined(dissonanceID); //tell dissonance
                        comms.comms.TrackPlayerPosition(this); //tell dissonance to track the remote player
                    }
                    break;
                }
            case "x": //speaking state
                {
                    if (message[0]==0)
                    {
                        comms.playerStoppedSpeaking(dissonanceID);
                        isSpeaking = false;
                    }
                    else
                    {
                     
                        comms.playerStartedSpeaking(dissonanceID);
                        isSpeaking = true;
                    }
                    break;
                }
        }
    }

    // Start is called before the first frame update
    void Start()
    {

        //handle dissonance stuff
        comms = GameObject.FindObjectOfType<VelCommsNetwork>();
        if(comms != null)
        {
            if (owner.isLocal)
            {
                setDissonanceID(comms.dissonanceId);
                comms.voiceQueued += sendVoiceData;

                //we also need to know when other players join, so we can send the dissonance ID again

                owner.manager.onPlayerJoined += (player) =>
                {
                    byte[] b = Encoding.UTF8.GetBytes(dissonanceID);
                    owner.sendMessage(this, "d", b);
                };
                owner.manager.setupMessageGroup("close", closePlayers.ToArray());
            }
        }
        if (owner.isLocal)
        {
            StartCoroutine(syncBehavior());
        }
    }

    void sendVoiceData(ArraySegment<byte> data)
    {
        //need to send it
        if(owner != null && owner.isLocal)
        {
            byte[] toSend = new byte[data.Count+4];
            byte[] lastAudioIdBytes = BitConverter.GetBytes(lastAudioId++);
            Buffer.BlockCopy(lastAudioIdBytes, 0, toSend, 0, 4);
            Buffer.BlockCopy(data.Array, data.Offset, toSend, 4, data.Count);
            owner.sendGroupMessage(this,"close", "a", toSend, false); //send voice data unreliably
        }
    }

    public void setDissonanceID(string id) //this sort of all initializes dissonance
    {
        dissonanceID = id;
        byte[] b = Encoding.UTF8.GetBytes(dissonanceID);
        owner.sendMessage(this, "d", b);
        comms.comms.TrackPlayerPosition(this);
    }

    void voiceInitialized(string id)
    {
        dissonanceID = id;
    }

    void OnDestroy()
    {
        comms.playerLeft(dissonanceID);
    }
    

    IEnumerator syncBehavior()
    {
        while (true)
        {
            owner.sendMessage(this, "s", getSyncMessage());
            yield return new WaitForSeconds(.1f);
        }
    }
    // Update is called once per frame
    void Update()
    {
        if (owner != null && owner.isLocal) {

            PlayerController[] players = GameObject.FindObjectsOfType<PlayerController>();
            bool shouldUpdate = false;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == this) { continue; }
                float dist = Vector3.Distance(players[i].transform.position, this.transform.position);
                if (dist < 2 && !closePlayers.Contains(players[i].owner.userid))
                {
                    closePlayers.Add(players[i].owner.userid);
                    shouldUpdate = true;
                }
                else if(dist >=2 && closePlayers.Contains(players[i].owner.userid))
                {
                    closePlayers.Remove(players[i].owner.userid);
                    shouldUpdate = true;
                }
            }
            if (shouldUpdate)
            {
                owner.manager.setupMessageGroup("close", closePlayers.ToArray());
            }
        }


        if (owner != null && !owner.isLocal)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, .1f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, .1f);
        }
        else if(owner != null && owner.isLocal)
        {

            //handle dissonance comms
            
            //if we're not speaking, and the comms say we are, send a speaking event, which will be received on other network players and sent to their comms accordingly
            if (comms.comms.FindPlayer(dissonanceID).IsSpeaking != isSpeaking) //unfortunately, there does not seem to be an event for this
            {
                isSpeaking = !isSpeaking;
                byte[] toSend = new byte[1];
                toSend[0] = isSpeaking ? (byte)1 : (byte)0;
                owner.sendMessage(this, "x", toSend);
                
                if (!isSpeaking)
                {
                    lastAudioId = 0;
                }

            }

            

            Vector3 movement = new Vector3();
            movement.x += Input.GetAxis("Horizontal");
            movement.y += Input.GetAxis("Vertical");
            movement.z = 0;
            transform.Translate(movement * Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.Space))
            {
                owner.networkInstantiate("TestNetworkedGameObject");
            }

            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                foreach(KeyValuePair<string,NetworkObject> kvp in owner.manager.objects)
                {
                    owner.takeOwnership(kvp.Key);
                }
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                foreach (KeyValuePair<string, NetworkObject> kvp in owner.manager.objects)
                {
                    owner.networkDestroy(kvp.Key);
                }
            }
        }
    }
}
