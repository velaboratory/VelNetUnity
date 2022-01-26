using System;
using System.Collections;
using UnityEngine;

namespace VelNet
{
	public abstract class NetworkSerializedObject : NetworkComponent
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
					if (IsMine)
					{
						byte[] newBytes = SendState();
						if (hybridOnChangeCompression)
						{
							if (Time.timeAsDouble - lastSendTime > slowSendInterval || !BinaryWriterExtensions.BytesSame(lastSentBytes, newBytes))
							{
								SendBytes(newBytes);
							}
						}
						else
						{
							SendBytes(newBytes);
						}

						lastSendTime = Time.timeAsDouble;
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
			ReceiveState(message);
		}

		protected abstract byte[] SendState();

		protected abstract void ReceiveState(byte[] message);
	}
}