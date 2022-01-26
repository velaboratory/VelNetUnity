using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace VelNet
{
	public abstract class NetworkSerializedObjectStream : NetworkComponent
	{
		[Tooltip("Send rate of this object. This caps out at the framerate of the game.")]
		public float serializationRateHz = 30;

		/// <summary>
		/// If the data hasn't changed, only sends updates across the network at 1Hz
		/// </summary>
		public bool hybridOnChangeCompression = true;

		private byte[] lastSentBytes;
		private double lastSendTime;
		private const double slowSendInterval = 2;


		protected virtual void Awake()
		{
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
						using MemoryStream mem = new MemoryStream();
						using BinaryWriter writer = new BinaryWriter(mem);
						SendState(writer);

						byte[] newBytes = mem.ToArray();
						if (hybridOnChangeCompression)
						{
							if (Time.timeAsDouble - lastSendTime > slowSendInterval || !BinaryWriterExtensions.BytesSame(lastSentBytes, newBytes))
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
			using MemoryStream mem = new MemoryStream(message);
			using BinaryReader reader = new BinaryReader(mem);

			ReceiveState(reader);
		}

		protected abstract void SendState(BinaryWriter binaryWriter);

		protected abstract void ReceiveState(BinaryReader binaryReader);
	}
}