using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace VelNet
{
	public class VelVoicePlayer : NetworkComponent
	{

		public VelVoice voiceSystem; //must be set for the player only
		public AudioSource source; //must be set for the clone only
		AudioClip myClip;
		public int bufferedAmount = 0;
		public int playedAmount = 0;
		int lastTime = 0;
		float[] empty = new float[1000]; //a buffer of 0s to force silence, because playing doesn't stop on demand
		float delayStartTime;
		public override void ReceiveBytes(byte[] message)
		{
			
			float[] temp = voiceSystem.decodeOpusData(message, message.Length);
			myClip.SetData(temp, bufferedAmount % source.clip.samples);
			bufferedAmount += temp.Length;
			myClip.SetData(empty, bufferedAmount % source.clip.samples); //buffer some empty data because otherwise you'll hear sound (but it'll be overwritten by the next sample)

			if (!source.isPlaying)
			{
				delayStartTime = Time.time; //I've received a packet but I haven't played it
			}
		}


		// Start is called before the first frame update
		void Start()
		{
			voiceSystem = GameObject.FindObjectOfType<VelVoice>();
			if (voiceSystem == null)
			{
				Debug.LogError("No microphone found.  Make sure you have one in the scene.");
				return;
			}
			if (networkObject.IsMine)
			{
				voiceSystem.encodedFrameAvailable += (frame) =>
				{
				//float[] temp = new float[frame.count];
				//System.Array.Copy(frame.array, temp, frame.count);
					MemoryStream mem = new MemoryStream();
					BinaryWriter writer = new BinaryWriter(mem);
					writer.Write(frame.array, 0, frame.count);
					this.SendBytes(mem.ToArray(), false);



				};
			}

			myClip = AudioClip.Create(this.name, 16000 * 10, 1, 16000, false);
			source.clip = myClip;
			source.loop = true;
			source.Pause();

		}

		

		// Update is called once per frame
		void Update()
		{
			
			

			if (bufferedAmount > playedAmount)
			{

				var offset = bufferedAmount - playedAmount;
				if ((offset > 1000) || (Time.time - delayStartTime) > .1f) //this seems to make the quality better
				{
					var temp = Mathf.Max(0, offset - 2000); 
					source.pitch = 1 + temp / 18000.0f; //okay to behind by 2000, but speed up real quick if by 170000
					

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