using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace VelNet
{
	public abstract class NetworkSerializedObject : NetworkComponent
	{
		[FormerlySerializedAs("updateRateHz")] [Tooltip("Send rate of this object")]
		public float serializationRateHz = 30;

		private void Start()
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

				yield return new WaitForSeconds(1 / serializationRateHz);
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