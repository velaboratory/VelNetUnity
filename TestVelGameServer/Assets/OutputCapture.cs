using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System;
using System.IO;
public class OutputCapture : MonoBehaviour
{

    float[] buffer = new float[100000];
    int curBufferPos = 0;
    int sampleRate;
    bool isPlaying = false;
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
        }
        
    }
    private void OnApplicationQuit()
    {
        
        
    }
}
