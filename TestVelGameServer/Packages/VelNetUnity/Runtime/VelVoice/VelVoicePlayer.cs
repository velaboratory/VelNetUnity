using System.IO;
using UnityEngine;
using System;
using Concentus.Structs;
namespace VelNet.Voice
{
	public class VelVoicePlayer : NetworkComponent
	{
		/// <summary>
		/// must be set for the player only
		/// </summary>
		public VelVoice voiceSystem;
		private OpusDecoder opusDecoder;
		/// <summary>
		/// must be set for the clone only
		/// </summary>
		public AudioSource source;

		private AudioClip myClip;
		public int bufferedAmount;
		public int playedAmount;
		private int lastTime;

		public Action<byte[]> DecodedVoiceDataReceived;
		public Action<byte[]> EncodedVoiceDataReceived;
		public Action<byte[]> EncodedVoiceDataSent;

		/// <summary>
		/// a buffer of 0s to force silence, because playing doesn't stop on demand
		/// </summary>
		private readonly float[] empty = new float[1000];

		private float delayStartTime;

		public override void ReceiveBytes(byte[] message)
		{
			EncodedVoiceDataReceived?.Invoke(message);
			float[] temp = voiceSystem.DecodeOpusData(opusDecoder, message, message.Length);
			DecodedVoiceDataReceived?.Invoke(message);
			myClip.SetData(temp, bufferedAmount % source.clip.samples);
			bufferedAmount += temp.Length;
			myClip.SetData(empty, bufferedAmount % source.clip.samples); //buffer some empty data because otherwise you'll hear sound (but it'll be overwritten by the next sample)

			if (!source.isPlaying)
			{
				delayStartTime = Time.time; //I've received a packet but I haven't played it
			}
		}

		// Start is called before the first frame update
		private void Start()
		{
			
			voiceSystem = GameObject.FindAnyObjectByType<VelVoice>();
			
			if (voiceSystem == null)
			{
				Debug.LogError("No microphone found.  Make sure you have one in the scene.");
				return;
			}

			opusDecoder = new OpusDecoder(voiceSystem.opusFreq, 1);

			if (networkObject.IsMine)
			{
				voiceSystem.encodedFrameAvailable += handleEncodedFrame;
			}

			myClip = AudioClip.Create(this.name, 16000 * 10, 1, 16000, false);
			source.clip = myClip;
			source.loop = true;
			source.Pause();
		}
		
		private void OnDestroy(){
			
			if(voiceSystem != null){
				voiceSystem.encodedFrameAvailable -= handleEncodedFrame;
			}
			
		}
		
		private void handleEncodedFrame(VelVoice.FixedArray frame) 
		{
			//float[] temp = new float[frame.count];
			//System.Array.Copy(frame.array, temp, frame.count);
			MemoryStream mem = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(mem);
			writer.Write(frame.array, 0, frame.count);
			byte[] toSend = mem.ToArray();
			this.SendBytes(toSend, false);
			EncodedVoiceDataSent?.Invoke(toSend);
		}


		// Update is called once per frame
		private void Update()
		{
			if (bufferedAmount > playedAmount)
			{
				int offset = bufferedAmount - playedAmount;
				if ((offset > 1000) || (Time.time - delayStartTime) > .1f) //this seems to make the quality better
				{
					int temp = Mathf.Max(0, offset - 2000);
					source.pitch = Mathf.Min(2, 1 + temp / 18000.0f); //okay to behind by 2000.  These numbers correspond to about 2x speed at a seconds behind


					if (!source.isPlaying)
					{
						source.Play();
					}
				}
				else
				{
					return;
				}
			}
			else if (playedAmount >= bufferedAmount)
			{
				playedAmount = bufferedAmount;
				source.Pause();
				source.timeSamples = bufferedAmount % source.clip.samples;
			}

			//Debug.Log(playedAmount);
			if (source.timeSamples >= lastTime)
			{
				playedAmount += (source.timeSamples - lastTime);
			}
			else //repeated
			{
				int total = source.clip.samples - lastTime + source.timeSamples;
				playedAmount += total;
			}

			lastTime = source.timeSamples;
		}
	}
}
