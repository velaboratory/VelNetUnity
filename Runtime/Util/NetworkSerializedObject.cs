using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace VelNet
{
	public abstract class NetworkSerializedObject : NetworkComponent
	{
		[Tooltip("Send rate of this object. This caps out at the framerate of the game.")]
		public float serializationRateHz = 30;

		private void Awake()
		{
			StartCoroutine(SendMessageUpdate());
		}

		private IEnumerator SendMessageUpdate()
		{
			while (true)
			{
				if (IsMine)
				{
					SendBytes(SendState());
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