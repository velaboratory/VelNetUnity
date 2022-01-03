using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System;
using System.IO;
public class OutputCapture : MonoBehaviour
{
    public velmicrophone mic;
    public float[] buffer = new float[100000]; //represents the last 100000 samples (roughly)
    public int curBufferPos = 0;
    public double outputTime; //time of curBufferPos
    int sampleRate;
    bool isPlaying = false;
    public long sampleNumber = 0;
    short[] lastPlayed = new short[512];
    byte[] lastPlayedBytes = new byte[1024];
    // Start is called before the first frame update
    void Awake()
    {
        
        sampleRate = AudioSettings.outputSampleRate;

    }

    // Update is called once per frame
    void Update()
    {

        isPlaying = Application.isPlaying;
        

    }

    private void OnAudioFilterRead(float[] data, int channels)
    {

        for(int i = 0; i < data.Length; i+=2)
        {
            lastPlayed[i / 2] = (short)(((data[i] + data[i + 1])/2.0f)*short.MaxValue);
        }

        if(mic.filter != null)
        {
            Buffer.BlockCopy(lastPlayed, 0, lastPlayedBytes, 0, 1024);
            lock (mic.filter)
            {
                mic.filter.RegisterFramePlayed(lastPlayedBytes);
            }
        }
        

        if (!isPlaying)
        {
            return;
        }

        lock (buffer)
        {
            if (curBufferPos + data.Length < buffer.Length)
            {
                System.Array.Copy(data, 0, buffer, curBufferPos, data.Length);
            }
            else
            {
                int numLeft = buffer.Length - curBufferPos;
                System.Array.Copy(data, 0, buffer, curBufferPos, numLeft);
                System.Array.Copy(data, buffer.Length - curBufferPos, buffer, 0, data.Length - numLeft);
                curBufferPos = numLeft;
            }
            
            outputTime = AudioSettings.dspTime;
        }
        
    }

    private float getSampleAtTime(double t)
    {
        lock (buffer)
        {
            
        }
        return 0;
    } 
    private void OnApplicationQuit()
    {
        
        
    }
}
