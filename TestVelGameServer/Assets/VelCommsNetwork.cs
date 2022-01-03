using System;
using System.Collections;
using System.Collections.Generic;
using Dissonance;
using Dissonance.Extensions;
using Dissonance.Networking;
using UnityEngine;

namespace Dissonance
{
    public class VelCommsNetwork : MonoBehaviour, ICommsNetwork
    {
        public ConnectionStatus Status
        {
            get
            {
                return manager.connected?ConnectionStatus.Connected:ConnectionStatus.Disconnected;
            }
        }

        public NetworkMode Mode
        {
            get
            {
                return NetworkMode.Client;
            }
        }

        public event Action<NetworkMode> ModeChanged;
        public event Action<string, CodecSettings> PlayerJoined;
        public event Action<string> PlayerLeft;
        public event Action<VoicePacket> VoicePacketReceived;
        public event Action<TextMessage> TextPacketReceived;
        public event Action<string> PlayerStartedSpeaking;
        public event Action<string> PlayerStoppedSpeaking;
        public event Action<RoomEvent> PlayerEnteredRoom;
        public event Action<RoomEvent> PlayerExitedRoom;

        ConnectionStatus _status = ConnectionStatus.Disconnected;
        CodecSettings initSettings;
        public string dissonanceId;
        List<VoicePacket> packets = new List<VoicePacket>();
        bool loopBackSound = false;
        public DissonanceComms comms;
        public NetworkManager manager;
        NetworkPlayer myPlayer;
        public void Initialize(string playerName, Rooms rooms, PlayerChannels playerChannels, RoomChannels roomChannels, CodecSettings codecSettings)
        {
            dissonanceId = playerName;
            initSettings = codecSettings;
            Debug.Log("Initializing dissonance");
            
            manager.onJoinedRoom += (player) =>
            {
                myPlayer = player;
                myPlayer.commsNetwork = this;
                myPlayer.setDissonanceID(playerName); //need to let that new player know my dissonance id (tell everyone again)
                
            };

            manager.onPlayerJoined += (player) =>
            {
                myPlayer.setDissonanceID(playerName); //need to let that new player know my dissonance id (tell everyone again)

                player.commsNetwork = this; //this will tell us when various things happen of importance

            };

        }

        public void voiceReceived(string sender,byte[] data,uint sequenceNumber)
        {
            VoicePacket vp = new VoicePacket(sender, ChannelPriority.Default, 1, false, new ArraySegment<byte>(data), sequenceNumber);
            VoicePacketReceived(vp);
        }

        public void SendText(string data, ChannelType recipientType, string recipientId)
        {
            Debug.Log("sending text");
        }

        public void SendVoice(ArraySegment<byte> data)
        {
            myPlayer?.sendAudioData(data);  
        }



        // Start is called before the first frame update
        void Start()
        {
            _status = ConnectionStatus.Connected;
            comms = GetComponent<DissonanceComms>();

        }

        public void playerJoined(string id)
        {
            PlayerJoined(id, initSettings);
            RoomEvent re = new RoomEvent();
            re.Joined = true;
            re.Room = "Global";
            re.PlayerName = id;
            PlayerEnteredRoom(re);
        }

        public void playerStartedSpeaking(string id)
        {
            PlayerStartedSpeaking(id);
            Debug.Log("player " + id + " started speaking");
        }
        public void playerStoppedSpeaking(string id)
        {
            PlayerStoppedSpeaking(id);
            Debug.Log("player " + id + " stopped speaking");
        }

    }
}
