using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System;

public class NetworkPlayer : MonoBehaviour
{
    public int userid;
    public string username;
    public string room;
    public NetworkManager manager;
    Vector3 networkPosition;
    velmicrophone mic;
    AudioClip speechClip;
    AudioSource source;
    bool buffering = false;
    public List<FixedArray> toSend = new List<FixedArray>();

    public bool isLocal = false;
    int lastSample = 0;
    int lastPlayTime = 0;
    int totalbuffered = 0;
    int totalPlayed = 0;
    // Start is called before the first frame update
    void Start()
    {
        source = GetComponent<AudioSource>();
        speechClip = AudioClip.Create("speechclip", 160000, 1, 16100, false);
        source.clip = speechClip;
        source.Stop();
        source.loop = true;
        source.spatialBlend = 0;
        if (manager.userid == userid) //this is me, I send updates
        {
            StartCoroutine(syncTransformCoroutine());
        }
    }

    public void attachMic(velmicrophone mic)
    {
        this.mic = mic;
        this.mic.encodedFrameAvailable += this.sendAudioData;
    }


    // Update is called once per frame
    void Update()
    {
        if (userid != manager.userid) //not me, I move to wherever I should
        {
            transform.position = Vector3.Lerp(transform.position, networkPosition, .1f);
        }
        else
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 delta = h * Vector3.right + v * Vector3.up;
            transform.position = transform.position + delta * Time.deltaTime;
        }

        //we also need to deal with the audio source

        int numSamplesPlayedSinceLast = source.timeSamples - lastPlayTime;

        if(numSamplesPlayedSinceLast < 0) //looped
        {
            numSamplesPlayedSinceLast = source.clip.samples - lastPlayTime + source.timeSamples;
        }
        totalPlayed += numSamplesPlayedSinceLast;
        if(totalPlayed >= totalbuffered)
        {
            int left = numSamplesPlayedSinceLast - totalbuffered;
            source.Pause();
            
        }
        lastPlayTime = source.timeSamples;
        
        
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
                        
                        byte[] data = Convert.FromBase64String(sections[1]);
                        
                        this.receiveAudioData(mic.decodeOpusData(data));
                        break;
                    }
            }
        }

    }

    public void receiveAudioData(float[] data)
    {

        speechClip.SetData(data, lastSample);
        lastSample += data.Length;
        lastSample %= speechClip.samples;
        totalbuffered += data.Length;
        if (!source.isPlaying && !buffering)
        {
            StartCoroutine(startSoundIn(2));
        }
        

    }
    IEnumerator startSoundIn(int frames)
    {
        buffering = true;
        for(int i = 0; i < frames; i++)
        {
            yield return null;
        }
        source.Play();
        buffering = false;
    }
    public void sendAudioData(FixedArray a)
    {
        string b64_data = Convert.ToBase64String(a.array,0,a.count);
        manager.sendTo(1, "2,"+b64_data + ";");
    }
}
