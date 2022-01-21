using System.IO;
using UnityEngine;

namespace VelNet
{
	/// <summary>
	/// A simple class that will sync the position and rotation of a network object with a rigidbody
	/// </summary>
	[AddComponentMenu("VelNet/VelNet Sync Rigidbody")]
	[RequireComponent(typeof(Rigidbody))]
	public class SyncRigidbody : NetworkSerializedObjectStream
	{
		public bool useLocalTransform;
		[Tooltip("0 to disable.")]
		public float teleportDistance;
		[Tooltip("0 to disable.")]
		public float teleportAngle;

		public bool syncKinematic;
		public bool syncGravity;
		public bool syncVelocity;
		public bool syncAngularVelocity;

		private Vector3 targetPosition;
		private Quaternion targetRotation;
		private float distanceAtReceiveTime;
		private float angleAtReceiveTime;
		private Rigidbody rb;

		private void Start()
		{
			rb = GetComponent<Rigidbody>();
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
		protected override void SendState(BinaryWriter writer)
		{
			if (useLocalTransform)
			{
				writer.Write(transform.localPosition);
				writer.Write(transform.localRotation);
			}
			else
			{
				writer.Write(transform.position);
				writer.Write(transform.rotation);
			}

			// writer.Write((new bool[] {rb.isKinematic, rb.useGravity}).GetBitmasks());
			if (syncKinematic) writer.Write(rb.isKinematic);
			if (syncGravity) writer.Write(rb.useGravity);
			if (syncVelocity) writer.Write(rb.velocity);
			if (syncAngularVelocity) writer.Write(rb.angularVelocity);
		}

		/// <summary>
		/// This gets called whenever a message about the state of this object is received.
		/// Usually at serializationRateHz.
		/// </summary>
		protected override void ReceiveState(BinaryReader reader)
		{
			targetPosition = reader.ReadVector3();
			targetRotation = reader.ReadQuaternion();

			if (syncKinematic) rb.isKinematic = reader.ReadBoolean();
			if (syncGravity) rb.useGravity = reader.ReadBoolean();
			if (syncVelocity) rb.velocity = reader.ReadVector3();
			if (syncAngularVelocity) rb.angularVelocity = reader.ReadVector3();

			// record the distance from the target for interpolation
			if (useLocalTransform)
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.localPosition);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.localRotation);
				if (teleportDistance != 0 && teleportDistance < distanceAtReceiveTime)
				{
					transform.localPosition = targetPosition;
				}
				if (teleportAngle != 0 && teleportAngle < angleAtReceiveTime)
				{
					transform.localRotation = targetRotation;
				}
			}
			else
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.position);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.rotation);
				if (teleportDistance != 0 && teleportDistance < distanceAtReceiveTime)
				{
					transform.position = targetPosition;
				}
				if (teleportAngle != 0 && teleportAngle < angleAtReceiveTime)
				{
					transform.rotation = targetRotation;
				}
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