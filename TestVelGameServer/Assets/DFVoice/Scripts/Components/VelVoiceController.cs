using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DaikonForge.VoIP;
[RequireComponent(typeof(NetworkPlayer))]
public class VelVoiceController : VoiceControllerBase
{
    NetworkPlayer player;
    public override bool IsLocal
    {
        get
        {

            if(player == null)
            {
                return true;
            }
            else
            {
                return player.isLocal;
            }
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        player = GetComponent<NetworkPlayer>();
        init();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    protected override void OnAudioDataEncoded(VoicePacketWrapper encodedFrame)
    {
        byte[] headers = encodedFrame.ObtainHeaders();
        byte[] data = encodedFrame.RawData;
        player.sendAudioData(headers, data);
        //send the headers and data separately
        encodedFrame.ReleaseHeaders();

    }

    public void receiveAudioFrame(byte[] headers, byte[] data)
    {
        VoicePacketWrapper packet = new VoicePacketWrapper(headers, data);
        ReceiveAudioData(packet);
    }


    
}
