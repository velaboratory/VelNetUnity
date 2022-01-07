using System.IO;
using UnityEngine;


namespace VelNetUnity
{
	/// <summary>
	/// A simple class that will sync the position and rotation of a network object
	/// </summary>
	[AddComponentMenu("VelNetUnity/VelNet Sync Transform")]
	public class SyncTransform : NetworkSerializedObject
	{
		public Vector3 targetPosition;
		public Quaternion targetRotation;
		public float smoothness = .1f;

		protected override byte[] SendState()
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);

			writer.Write(transform.position);
			writer.Write(transform.rotation);

			return mem.ToArray();
		}

		protected override void ReceiveState(byte[] message)
		{
			using MemoryStream mem = new MemoryStream(message);
			using BinaryReader reader = new BinaryReader(mem);

			targetPosition = reader.ReadVector3();
			targetRotation = reader.ReadQuaternion();
		}

		private void Update()
		{
			if (IsMine) return;
			transform.position = Vector3.Lerp(transform.position, targetPosition, 1 / smoothness / serializationRateHz);
			transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1 / smoothness / serializationRateHz);
		}
	}
}