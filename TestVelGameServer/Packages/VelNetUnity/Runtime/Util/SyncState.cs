using System;
using System.Collections;
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
		private int lastSentLength;
		private double lastSendTime;
		private const double slowSendInterval = 2;

		private NetworkWriter writer;
		private NetworkReader reader;

		protected virtual void Awake()
		{
			writer = new NetworkWriter();
			reader = new NetworkReader();

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
						writer.Reset();
						SendState(writer);

						int newLength = writer.Length;
						byte[] newBuffer = writer.Buffer;

						if (hybridOnChangeCompression)
						{
							if (Time.timeAsDouble - lastSendTime > slowSendInterval ||
							    !BinaryWriterExtensions.BytesSame(lastSentBytes, lastSentLength, newBuffer, newLength))
							{
								SendBytes(newBuffer, 0, newLength);
								lastSendTime = Time.timeAsDouble;
							}
						}
						else
						{
							SendBytes(newBuffer, 0, newLength);
							lastSendTime = Time.timeAsDouble;
						}

						// Update lastSentBytes only if the data changed or first send
						if (!BinaryWriterExtensions.BytesSame(lastSentBytes, lastSentLength, newBuffer, newLength))
						{
							if (lastSentBytes == null || lastSentBytes.Length < newLength)
							{
								lastSentBytes = new byte[newLength];
							}
							Array.Copy(newBuffer, lastSentBytes, newLength);
							lastSentLength = newLength;
						}
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

		public override void ReceiveBytes(NetworkReader networkReader)
		{
			UnpackState(networkReader);
		}

		protected abstract void SendState(NetworkWriter networkWriter);

		protected abstract void ReceiveState(NetworkReader networkReader);

		public void PackState(NetworkWriter networkWriter)
		{
			SendState(networkWriter);
		}

		public void UnpackState(NetworkReader networkReader)
		{
			ReceiveState(networkReader);
		}

		public void ForceSync()
		{
			writer.Reset();
			SendState(writer);
			SendBytes(writer.Buffer, 0, writer.Length);
		}
	}
}
