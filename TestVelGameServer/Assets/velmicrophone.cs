using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Concentus.Structs;
using System.Threading;
using System;
using AudioProcessingModuleCs.Media.Dsp.WebRtc;
using AudioProcessingModuleCs.Media;
public class velmicrophone : MonoBehaviour
{
    OpusEncoder opusEncoder;
    OpusDecoder opusDecoder;
    public OutputCapture oc = null;
    //StreamWriter sw;
    AudioClip clip;
    float[] tempData;
    short[] encoderBuffer;
    List<short[]> frameBuffer;

    List<FixedArray> sendQueue = new List<FixedArray>();
    List<short[]> encoderArrayPool = new List<short[]>();
    List<FixedArray> decoderArrayPool = new List<FixedArray>();
    int lastUsedEncoderPool = 0;
    int lastUsedDecoderPool = 0;
    int encoderBufferIndex=0;
    int lastPosition = 0;
    string device = "";
    int encoder_frame_size = 640;
    double micSampleTime = 1 / 44100.0;
    double encodeTime = 1 / 16000.0;
    double lastMicSample; //holds the last mic sample, in case we need to interpolate it
    double sampleTimer = 0; //increments with every mic sample, but when over the encodeTime, causes a sample and subtracts that encode time
    EventWaitHandle waiter;
    float silenceThreshold = .04f; //average volume of packet
    int numSilent = 0; //number of silent packets detected
    int minSilencePacketsToStop = 2;
    double averageVolume = 0;
    Thread t;
    public Action<FixedArray> encodedFrameAvailable = delegate { };
    double micTime = 0;
    long sampleNumber = 0;
    public int offset = 512;
    public double gain = 2.0f;
    public WebRtcFilter filter = null;
    // Start is called before the first frame update
    void Start()
    {
        opusEncoder = new OpusEncoder(16000, 1, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
        opusDecoder = new OpusDecoder(16000, 1);
        encoderBuffer = new short[16000];
        frameBuffer = new List<short[]>();
        //string path = Application.persistentDataPath + "/" + "mic.csv"; //this was for writing mic samples
        //sw = new StreamWriter(path, false);




        for(int i = 0; i < 100; i++) //pre allocate a bunch of arrays for microphone frames (probably will only need 1 or 2)
        {
            encoderArrayPool.Add(new short[encoder_frame_size]);
            decoderArrayPool.Add(new FixedArray(1000));
            
        }
        
        t = new Thread(encodeThread);
        waiter = new EventWaitHandle(true, EventResetMode.AutoReset);
        t.Start();
    }

    public void startMicrophone(string mic)
    {
        Debug.Log(mic);
        device = mic;
        int minFreq, maxFreq;
        Microphone.GetDeviceCaps(device, out minFreq, out maxFreq);
        Debug.Log("Freq: " + minFreq + ":" + maxFreq);
        clip = Microphone.Start(device, true, 1, 44100);


        Debug.Log("Frequency:" + clip.frequency);
        tempData = new float[clip.samples * clip.channels];
        Debug.Log("channels: " + clip.channels);

        filter = new WebRtcFilter(256, 640, new AudioFormat(16000), new AudioFormat(48000), false, false, false);
        
    }
   
    private void OnApplicationQuit()
    {
        t.Abort();
        
        //sw.Flush();
        //sw.Close();
        
    }

    short[] getNextEncoderPool()
    {
        lastUsedEncoderPool = (lastUsedEncoderPool + 1) % encoderArrayPool.Count;
        return encoderArrayPool[lastUsedEncoderPool];
    }

    FixedArray getNextDecoderPool()
    {
        lastUsedDecoderPool = (lastUsedDecoderPool + 1) % decoderArrayPool.Count;

        FixedArray toReturn = decoderArrayPool[lastUsedDecoderPool];
        toReturn.count = 0;
        return toReturn;
    }
    // Update is called once per frame
    void Update()
    {
        if (clip != null)
        {
            
            int micPosition = Microphone.GetPosition(device);
            if(micPosition == lastPosition)
            {
                return; //sometimes the microphone will not advance
            }
            
            int numSamples = 0;
            float[] temp;
            if (micPosition > lastPosition)
            {
                numSamples = micPosition - lastPosition;
            }
            else
            {
                //whatever was left
                numSamples = (tempData.Length - lastPosition) + micPosition;
            }

            

            //Debug.Log(micPosition);
            temp = new float[numSamples];  //this has to be dynamically allocated because of the way clip.GetData works (annoying...maybe use native mic)

            clip.GetData(temp, lastPosition);
            lastPosition = micPosition;

            

            //this code does 2 things.  1) it samples the microphone data to be exactly what the encoder wants, 2) it forms encoder packets



            for (int i = 0; i < temp.Length; i++) //iterate through temp, which contans that mic samples at 44.1khz
            {


                


                sampleTimer += micSampleTime;
                if(sampleTimer >= encodeTime)
                {
                    
                    //take a sample between the last sample and the current sample


                    double diff = sampleTimer - encodeTime; //this represents how far past this sample actually is
                    double t = diff / micSampleTime; //this should be between 0 and 1
                    double v = lastMicSample * (1 - t) + temp[i] * t;


                    sampleTimer -= encodeTime; 

                    encoderBuffer[encoderBufferIndex++] = (short)(v*short.MaxValue);
                    averageVolume += v > 0 ? v : -v;
                    if(encoderBufferIndex > encoder_frame_size) //this is when a new packet gets created
                    {

                        
                        

                        averageVolume = averageVolume / encoder_frame_size;

                        if(averageVolume < silenceThreshold)
                        {
                            numSilent++;
                        }
                        else
                        {
                            numSilent = 0;
                        }
                        averageVolume = 0;

                        if (numSilent < minSilencePacketsToStop)
                        {

                            short[] frame = getNextEncoderPool(); //these are predefined sizes, so we don't have to allocate a new array
                                                                  //lock the frame buffer

                            System.Array.Copy(encoderBuffer, frame, encoder_frame_size); //nice and fast

                            

                            byte[] recorded = new byte[frame.Length * 2];
                            Buffer.BlockCopy(frame, 0, recorded, 0, frame.Length * 2);

                            lock (filter)
                            {
                                filter.Write(recorded);
                            }

                            /*
                            bool moreFrames;

                            do
                            {
                                short[] cancelBuffer = new short[encoder_frame_size];
                                lock (filter)
                                {
                                    if (filter.Read(cancelBuffer, out moreFrames))
                                    {
                                        lock (frameBuffer)
                                        {

                                            frameBuffer.Add(cancelBuffer);
                                            waiter.Set(); //signal the encode frame
                                        }
                                    }
                                }
                            } while (moreFrames);
                            */
                            
                            lock (frameBuffer)
                            {

                                frameBuffer.Add(frame);
                                waiter.Set(); //signal the encode frame
                            }
                            


                        }
                        
                        encoderBufferIndex = 0;
                        
                    }
                } 
                lastMicSample = temp[i]; //remember the last sample, just in case this is the first one next time 
            }
        }

        lock (sendQueue)
        {
            foreach(FixedArray f in sendQueue)
            {
                encodedFrameAvailable(f);

            }
            sendQueue.Clear();
        }
        
    }

    public short[] decodeOpusData(byte[] data)
    {
        short[] t = getNextEncoderPool();
        opusDecoder.Decode(data, 0, data.Length, t, 0, encoder_frame_size);
        return t;
    }

    void encodeThread()
    {
        
        while (waiter.WaitOne(Timeout.Infinite)) //better to wait on signal
        {

            List<short[]> toEncode = new List<short[]>();
            
            
            lock (frameBuffer)
            {
                foreach (short[] frame in frameBuffer) { 
                    toEncode.Add(frame);
                }
                frameBuffer.Clear();
            }
        
            foreach(short[] frame in toEncode)
            {
                FixedArray a = getNextDecoderPool();
                int out_data_size = opusEncoder.Encode(frame, 0, encoder_frame_size, a.array, 0, 1000);
                a.count = out_data_size;
                //add frame to the send buffer
                lock (sendQueue)
                {
                    sendQueue.Add(a);
                }
            }

        }
        
    }


	
    
}
