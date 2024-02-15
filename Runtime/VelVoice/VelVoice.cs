using System.Collections.Generic;
using UnityEngine;
using Concentus.Structs;
using System.Threading;
using System;
using System.Linq;

namespace VelNet.Voice
{
	public class VelVoice : MonoBehaviour
	{
		public class FixedArray
		{
			public readonly byte[] array;
			public int count;

			public FixedArray(int max)
			{
				array = new byte[max];
				count = 0;
			}
		}

		private OpusEncoder opusEncoder;
		private OpusDecoder opusDecoder;

		//StreamWriter sw;
		private AudioClip clip;
		private float[] tempData;
		private float[] encoderBuffer;
		private List<float[]> frameBuffer;

		private readonly List<FixedArray> sendQueue = new List<FixedArray>();
		private readonly List<float[]> encoderArrayPool = new List<float[]>();
		private readonly List<FixedArray> decoderArrayPool = new List<FixedArray>();
		private int lastUsedEncoderPool;
		private int lastUsedDecoderPool;
		private int encoderBufferIndex;
		private int lastPosition;
		private const int encoderFrameSize = 640;
		private double micSampleTime;
		private const int opusFreq = 16000;
		private const double encodeTime = 1 / (double)16000;

		/// <summary>
		/// holds the last mic sample, in case we need to interpolate it
		/// </summary>
		private double lastMicSample;

		/// <summary>
		/// increments with every mic sample, but when over the encodeTime, causes a sample and subtracts that encode time
		/// </summary>
		private double sampleTimer;

		private EventWaitHandle waiter;

		/// <summary>
		/// average volume of packet
		/// </summary>
		public float silenceThreshold = .01f;

		/// <summary>
		/// number of silent packets detected
		/// </summary>
		private int numSilent;

		public int minSilencePacketsToStop = 10;
		private double averageVolume;
		private Thread t;
		public Action<FixedArray> encodedFrameAvailable = delegate { };

		public bool autostartMicrophone = true;

		private void Start()
		{
			opusEncoder = new OpusEncoder(opusFreq, 1, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
			opusDecoder = new OpusDecoder(opusFreq, 1);
			encoderBuffer = new float[opusFreq];
			frameBuffer = new List<float[]>();

			// pre allocate a bunch of arrays for microphone frames (probably will only need 1 or 2)
			for (int i = 0; i < 100; i++)
			{
				encoderArrayPool.Add(new float[encoderFrameSize]);
				decoderArrayPool.Add(new FixedArray(encoderFrameSize));
			}

			t = new Thread(EncodeThread);
			waiter = new EventWaitHandle(true, EventResetMode.AutoReset);
			t.Start();

			if (autostartMicrophone)
			{
				StartMicrophone();
			}
		}

		/// <summary>
		/// Starts VelVoice with the default microphone.
		/// </summary>
		public void StartMicrophone()
		{
#if !UNITY_WEBGL && !UNITY_EDITOR
			StartMicrophone(Microphone.devices.FirstOrDefault());
#endif
		}

		/// <summary>
		/// Starts VelVoice
		/// </summary>
		/// <param name="micDeviceName">The device name of the microphone to record with</param>
		public void StartMicrophone(string micDeviceName)
		{
#if !UNITY_WEBGL && !UNITY_EDITOR
			Debug.Log("Starting with microphone: " + micDeviceName);
			if (micDeviceName == null) return;
			device = micDeviceName;
			Microphone.GetDeviceCaps(device, out int minFreq, out int maxFreq);
			Debug.Log("Freq: " + minFreq + ":" + maxFreq);
			clip = Microphone.Start(device, true, 10, 48000);
			micSampleTime = 1.0 / clip.frequency;

			Debug.Log("Frequency:" + clip.frequency);
			tempData = new float[clip.samples * clip.channels];
			Debug.Log("channels: " + clip.channels);
#endif
		}

		private void OnApplicationQuit()
		{
			t.Abort();

			//sw.Flush();
			//sw.Close();
		}

		private float[] GetNextEncoderPool()
		{
			lastUsedEncoderPool = (lastUsedEncoderPool + 1) % encoderArrayPool.Count;
			return encoderArrayPool[lastUsedEncoderPool];
		}

		private FixedArray GetNextDecoderPool()
		{
			lastUsedDecoderPool = (lastUsedDecoderPool + 1) % decoderArrayPool.Count;

			FixedArray toReturn = decoderArrayPool[lastUsedDecoderPool];
			toReturn.count = 0;
			return toReturn;
		}

		// Update is called once per frame
		private void Update()
		{
#if !UNITY_WEBGL && !UNITY_EDITOR
			if (clip != null)
			{
				
				int micPosition = Microphone.GetPosition(device);
				if (micPosition == lastPosition)
				{
					return; //sometimes the microphone will not advance
				}

				int numSamples;
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

				// this has to be dynamically allocated because of the way clip.GetData works (annoying...maybe use native mic)
				temp = new float[numSamples];
				clip.GetData(temp, lastPosition);
				lastPosition = micPosition;


				// this code does 2 things.  1) it samples the microphone data to be exactly what the encoder wants, 2) it forms encoder packets
				// iterate through temp, which contains that mic samples at 44.1khz
				foreach (float sample in temp)
				{
					sampleTimer += micSampleTime;
					if (sampleTimer > encodeTime)
					{
						//take a sample between the last sample and the current sample

						double diff = sampleTimer - encodeTime; //this represents how far past this sample actually is
						double t = diff / micSampleTime; //this should be between 0 and 1
						double v = lastMicSample * (1 - t) + sample * t;
						sampleTimer -= encodeTime;

						encoderBuffer[encoderBufferIndex++] = (float)v;
						averageVolume += v > 0 ? v : -v;
						if (encoderBufferIndex > encoderFrameSize) //this is when a new packet gets created
						{
							averageVolume = averageVolume / encoderFrameSize;

							if (averageVolume < silenceThreshold)
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
								float[] frame = GetNextEncoderPool(); //these are predefined sizes, so we don't have to allocate a new array
								//lock the frame buffer

								System.Array.Copy(encoderBuffer, frame, encoderFrameSize); //nice and fast


								lock (frameBuffer)
								{
									frameBuffer.Add(frame);
									waiter.Set(); //signal the encode frame
								}
							}

							encoderBufferIndex = 0;
						}
					}

					lastMicSample = sample; //remember the last sample, just in case this is the first one next time 
				}
			}

			lock (sendQueue)
			{
				foreach (FixedArray f in sendQueue)
				{
					encodedFrameAvailable(f);
				}

				sendQueue.Clear();
			}
#endif
		}

		public float[] DecodeOpusData(byte[] data, int count)
		{
			float[] t = GetNextEncoderPool();
			opusDecoder.Decode(data, 0, count, t, 0, encoderFrameSize);
			return t;
		}

		private void EncodeThread()
		{
			while (waiter.WaitOne(Timeout.Infinite)) //better to wait on signal
			{
				List<float[]> toEncode = new List<float[]>();


				lock (frameBuffer)
				{
					toEncode.AddRange(frameBuffer);
					frameBuffer.Clear();
				}

				foreach (float[] frame in toEncode)
				{
					FixedArray a = GetNextDecoderPool();
					int outDataSize = opusEncoder.Encode(frame, 0, encoderFrameSize, a.array, 0, a.array.Length);
					a.count = outDataSize;
					//add frame to the send buffer
					lock (sendQueue)
					{
						sendQueue.Add(a);
					}
				}
			}
		}
	}
}