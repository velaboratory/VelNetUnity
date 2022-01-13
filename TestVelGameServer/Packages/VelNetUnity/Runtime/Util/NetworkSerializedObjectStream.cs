using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;

namespace VelNet
{
	public abstract class NetworkSerializedObjectStream : NetworkComponent
	{
		[Tooltip("Send rate of this object. This caps out at the framerate of the game.")]
		public float serializationRateHz = 30;

		protected virtual void Awake()
		{
			StartCoroutine(SendMessageUpdate());
		}

		private IEnumerator SendMessageUpdate()
		{
			while (true)
			{
				if (IsMine)
				{
					using MemoryStream mem = new MemoryStream();
					using BinaryWriter writer = new BinaryWriter(mem);
					SendState(writer);
					SendBytes(mem.ToArray());
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