using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace VelNet
{
	public abstract class SyncState : NetworkComponent, IPackState
	{
		[Tooltip("Send rate of this object. This caps out at the framerate of the game.")]
		public float serializationRateHz = 30;

		/// <summary>
		/// If the data hasn't changed, only sends updates across the network at 0.5Hz
		/// </summary>
		public bool hybridOnChangeCompression = true;

		private byte[] lastSentBytes;
		private double lastSendTime;
		private const double slowSendInterval = 2;

		private MemoryStream writerMemory;
		private BinaryWriter writer;
		private MemoryStream readerMemory;
		private BinaryReader reader;

		protected virtual void Awake()
		{
			writerMemory = new MemoryStream();
			writer = new BinaryWriter(writerMemory);
			readerMemory = new MemoryStream();
			reader = new BinaryReader(readerMemory);

			StartCoroutine(SendMessageUpdate());
		}

		private IEnumerator SendMessageUpdate()
		{
			while (true)
			{
				try
				{
					if (IsMine && enabled)
					{
						byte[] newBytes = PackState();

						if (hybridOnChangeCompression)
						{
							if (Time.timeAsDouble - lastSendTime > slowSendInterval ||
							    !BinaryWriterExtensions.BytesSame(lastSentBytes, newBytes))
							{
								SendBytes(newBytes);
								lastSendTime = Time.timeAsDouble;
							}
						}
						else
						{
							SendBytes(newBytes);
							lastSendTime = Time.timeAsDouble;
						}

						lastSentBytes = newBytes;
					}
				}
				catch (Exception e)
				{
					Debug.LogError(e);
				}

				yield return new WaitForSeconds(1f / serializationRateHz);
			}
			// ReSharper disable once IteratorNeverReturns
		}

		public override void ReceiveBytes(byte[] message)
		{
			UnpackState(message);
		}

		protected abstract void SendState(BinaryWriter binaryWriter);

		protected abstract void ReceiveState(BinaryReader binaryReader);

		public byte[] PackState()
		{
			writerMemory.Position = 0;
			writerMemory.SetLength(0);
			SendState(writer);
			return writerMemory.ToArray();
		}

		public void UnpackState(byte[] state)
		{
			readerMemory.Position = 0;
			readerMemory.SetLength(0);
			readerMemory.Write(state, 0, state.Length);
			readerMemory.Position = 0;
			ReceiveState(reader);
		}

		public void ForceSync()
		{
			SendBytes(PackState());
		}
	}
}