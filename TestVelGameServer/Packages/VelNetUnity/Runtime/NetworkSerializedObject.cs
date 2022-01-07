using System;
using System.Timers;
using UnityEngine;
using UnityEngine.Serialization;

namespace VelNetUnity
{
	public abstract class NetworkSerializedObject : NetworkObject
	{
		[FormerlySerializedAs("updateRateHz")] [Tooltip("Send rate of this object")] public float serializationRateHz = 30;

		private void Start()
		{
			Timer timer = new Timer();
			timer.Interval = serializationRateHz;
			timer.Elapsed += SendMessageUpdate;
		}

		private void SendMessageUpdate(object sender, ElapsedEventArgs e)
		{
			if (owner != null && owner.isLocal)
			{
				owner.SendMessage(this, "s", SendState());
			}
		}

		protected abstract byte[] SendState();

		public override void HandleMessage(string identifier, byte[] message)
		{
			if (identifier == "s")
			{
				ReceiveState(message);
			}
		}

		protected abstract void ReceiveState(byte[] message);
	}
}