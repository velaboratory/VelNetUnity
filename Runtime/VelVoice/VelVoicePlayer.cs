using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Threading;
using Concentus.Structs;
using UnityEngine.Profiling;

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

		// ---- threaded decode ----------------------------------------------------
		// Opus decode used to run inside ReceiveBytes, which VelNetManager dispatches on
		// the MAIN thread — and Concentus is a managed (C#) decoder, so with many remote
		// speakers the render thread was spending real time decoding voice. ReceiveBytes
		// now just copies the packet into a pooled buffer and queues it; ONE shared
		// background thread does the decodes (a single consumer preserves per-player
		// packet order); Update() drains the decoded frames into AudioClip.SetData,
		// which is main-thread-only but is just a cheap copy.

		/// <summary>Samples per decoded voice frame (matches VelVoice's 40 ms @ 16 kHz).</summary>
		private const int FRAME_SIZE = 640;

		private struct EncodedPacket
		{
			public VelVoicePlayer player;
			public byte[] data;
			public int length;
		}

		private static readonly ConcurrentQueue<EncodedPacket> encodedQueue = new ConcurrentQueue<EncodedPacket>();
		private static readonly ConcurrentQueue<byte[]> packetPool = new ConcurrentQueue<byte[]>();  // encoded bytes
		private static readonly ConcurrentQueue<float[]> framePool = new ConcurrentQueue<float[]>(); // decoded frames
		private static readonly AutoResetEvent decodeSignal = new AutoResetEvent(false);
		private static Thread decodeThread;
		private static volatile bool decodeThreadStop;

		// Decoded frames awaiting the main thread (drained in Update -> AudioClip.SetData).
		private readonly ConcurrentQueue<float[]> decodedFrames = new ConcurrentQueue<float[]>();
		// Set on the main thread in OnDestroy; read by the decode thread. We avoid Unity's
		// overloaded null-check off the main thread, so this flag is the lifetime guard.
		private volatile bool destroyed;

		public override void ReceiveBytes(NetworkReader reader)
		{
			int n = reader.Remaining;
			if (n <= 0) return;

			byte[] message;
			if (EncodedVoiceDataReceived != null || DecodedVoiceDataReceived != null)
			{
				// External listeners get (and may keep) the array, so it can't come from the
				// pool. Note both events now fire on arrival, before the actual decode.
				message = reader.ReadBytes(n);
				EncodedVoiceDataReceived?.Invoke(message);
				DecodedVoiceDataReceived?.Invoke(message);
			}
			else
			{
				if (!packetPool.TryDequeue(out message) || message.Length < n)
				{
					message = new byte[Math.Max(n, 256)];
				}
				reader.ReadBytes(message, 0, n);
			}

			encodedQueue.Enqueue(new EncodedPacket { player = this, data = message, length = n });
			EnsureDecodeThread();
			decodeSignal.Set();
		}

		private static void EnsureDecodeThread()
		{
			if (decodeThread != null && decodeThread.IsAlive) return;
			decodeThreadStop = false;
			decodeThread = new Thread(DecodeLoop) { IsBackground = true, Name = "VelVoice Decode" };
			decodeThread.Start();
			// Editor domain reloads don't abort running threads; make ours exit so it can't
			// linger across play sessions. (IsBackground covers normal app shutdown.)
			AppDomain.CurrentDomain.DomainUnload += (_, __) =>
			{
				decodeThreadStop = true;
				decodeSignal.Set();
			};
		}

		private static void DecodeLoop()
		{
			Profiler.BeginThreadProfiling("VelNet", "Voice Decode Thread");
			while (!decodeThreadStop)
			{
				decodeSignal.WaitOne(250);
				while (encodedQueue.TryDequeue(out EncodedPacket p))
				{
					if (!framePool.TryDequeue(out float[] frame))
					{
						frame = new float[FRAME_SIZE];
					}

					bool decoded = false;
					try
					{
						// ReferenceEquals, not Unity's overloaded ==: we're off the main thread.
						if (!ReferenceEquals(p.player, null) && !p.player.destroyed && p.player.opusDecoder != null)
						{
							p.player.opusDecoder.Decode(p.data, 0, p.length, frame, 0, FRAME_SIZE);
							p.player.decodedFrames.Enqueue(frame);
							decoded = true;
						}
					}
					catch
					{
						// Malformed packet or a decoder racing destruction — drop the frame.
					}
					finally
					{
						if (!decoded && framePool.Count < 64) framePool.Enqueue(frame);
						if (packetPool.Count < 64) packetPool.Enqueue(p.data);
					}
				}
			}
			Profiler.EndThreadProfiling();
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

			destroyed = true;
			// Recycle anything the decode thread already produced for us.
			while (decodedFrames.TryDequeue(out float[] frame))
			{
				if (framePool.Count < 64) framePool.Enqueue(frame);
			}
			if(voiceSystem != null){
				voiceSystem.encodedFrameAvailable -= handleEncodedFrame;
			}

		}

		private void handleEncodedFrame(VelVoice.FixedArray frame)
		{
			this.SendBytes(frame.array, 0, frame.count, false);
			if (EncodedVoiceDataSent != null)
			{
				byte[] copy = new byte[frame.count];
				Array.Copy(frame.array, copy, frame.count);
				EncodedVoiceDataSent.Invoke(copy);
			}
		}


		// Update is called once per frame
		private void Update()
		{
			// Pull frames the decode thread finished and buffer them into the clip
			// (AudioClip.SetData is main-thread-only; it's just a copy, the expensive
			// Opus decode already happened on the worker).
			while (decodedFrames.TryDequeue(out float[] frame))
			{
				if (myClip != null && source != null && source.clip != null)
				{
					myClip.SetData(frame, bufferedAmount % source.clip.samples);
					bufferedAmount += frame.Length;
					myClip.SetData(empty, bufferedAmount % source.clip.samples); //buffer some empty data because otherwise you'll hear sound (but it'll be overwritten by the next sample)

					if (!source.isPlaying)
					{
						delayStartTime = Time.time; //I've received a packet but I haven't played it
					}
				}
				if (framePool.Count < 64) framePool.Enqueue(frame);
			}

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
