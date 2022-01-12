using System.IO;
using UnityEngine;


namespace VelNet
{
	/// <summary>
	/// A simple class that will sync the position and rotation of a network object
	/// </summary>
	[AddComponentMenu("VelNet/VelNet Sync Transform")]
	public class SyncTransform : NetworkSerializedObject
	{
		public bool useLocalTransform;

		private Vector3 targetPosition;
		private Quaternion targetRotation;
		private float distanceAtReceiveTime;
		private float angleAtReceiveTime;

		private void Start()
		{
			if (useLocalTransform)
			{
				targetPosition = transform.localPosition;
				targetRotation = transform.localRotation;
			}
			else
			{
				targetPosition = transform.position;
				targetRotation = transform.rotation;
			}
		}

		/// <summary>
		/// This gets called at serializationRateHz when the object is locally owned
		/// </summary>
		/// <returns>The state of this object to send across the network</returns>
		protected override byte[] SendState()
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);

			writer.Write(transform.localPosition);
			writer.Write(transform.localRotation);

			return mem.ToArray();
		}

		/// <summary>
		/// This gets called whenever a message about the state of this object is received.
		/// Usually at serializationRateHz.
		/// </summary>
		/// <param name="message">The network state of this object</param>
		protected override void ReceiveState(byte[] message)
		{
			using MemoryStream mem = new MemoryStream(message);
			using BinaryReader reader = new BinaryReader(mem);

			targetPosition = reader.ReadVector3();
			targetRotation = reader.ReadQuaternion();

			// record the distance from the target for interpolation
			if (useLocalTransform)
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.localPosition);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.localRotation);
			}
			else
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.position);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.rotation);
			}
		}

		private void Update()
		{
			if (IsMine) return;

			if (useLocalTransform)
			{
				transform.localPosition = Vector3.MoveTowards(
					transform.localPosition,
					targetPosition,
					Time.deltaTime * distanceAtReceiveTime * serializationRateHz
				);
				transform.localRotation = Quaternion.RotateTowards(
					transform.localRotation,
					targetRotation,
					Time.deltaTime * angleAtReceiveTime * serializationRateHz
				);
			}
			else
			{
				transform.position = Vector3.MoveTowards(
					transform.position,
					targetPosition,
					Time.deltaTime * distanceAtReceiveTime * serializationRateHz
				);
				transform.rotation = Quaternion.RotateTowards(
					transform.rotation,
					targetRotation,
					Time.deltaTime * angleAtReceiveTime * serializationRateHz
				);
			}
		}
	}
}