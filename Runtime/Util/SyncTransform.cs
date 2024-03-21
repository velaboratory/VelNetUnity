using System.IO;
using UnityEngine;

namespace VelNet
{
	/// <summary>
	/// A simple class that will sync the position and rotation of a network object
	/// </summary>
	[AddComponentMenu("VelNet/VelNet Sync Transform")]
	public class SyncTransform : SyncState
	{
		[Space] public bool position = true;
		public bool rotation = true;
		[Tooltip("Scale is always local")] public bool scale;

		[Space] public bool useLocalTransform;

		[Tooltip("0 to disable.")] public float teleportDistance;
		[Tooltip("0 to disable.")] public float teleportAngle;

		private Vector3 targetScale;
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

			targetScale = transform.localScale;
		}

		/// <summary>
		/// This gets called at serializationRateHz when the object is locally owned
		/// </summary>
		protected override void SendState(BinaryWriter writer)
		{
			if (useLocalTransform)
			{
				if (position) writer.Write(transform.localPosition);
				if (rotation) writer.Write(transform.localRotation);
			}
			else
			{
				if (position) writer.Write(transform.position);
				if (rotation) writer.Write(transform.rotation);
			}

			if (scale) writer.Write(transform.localScale);
		}

		/// <summary>
		/// This gets called whenever a message about the state of this object is received.
		/// Usually at serializationRateHz.
		/// </summary>
		protected override void ReceiveState(BinaryReader reader)
		{
			if (position) targetPosition = reader.ReadVector3();
			if (rotation) targetRotation = reader.ReadQuaternion();
			if (scale) targetScale = reader.ReadVector3();

			// record the distance from the target for interpolation
			if (useLocalTransform)
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.localPosition);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.localRotation);
				if (IsMine || teleportDistance != 0 && teleportDistance < distanceAtReceiveTime)
				{
					transform.localPosition = targetPosition;
				}

				if (IsMine || teleportAngle != 0 && teleportAngle < angleAtReceiveTime)
				{
					transform.localRotation = targetRotation;
				}
			}
			else
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.position);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.rotation);
				if (IsMine || teleportDistance != 0 && teleportDistance < distanceAtReceiveTime)
				{
					transform.position = targetPosition;
				}

				if (IsMine || teleportAngle != 0 && teleportAngle < angleAtReceiveTime)
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
				if (position)
				{
					transform.localPosition = Vector3.MoveTowards(
						transform.localPosition,
						targetPosition,
						Time.deltaTime * distanceAtReceiveTime * serializationRateHz
					);
				}

				if (rotation)
				{
					transform.localRotation = Quaternion.RotateTowards(
						transform.localRotation,
						targetRotation,
						Time.deltaTime * angleAtReceiveTime * serializationRateHz
					);
				}
			}
			else
			{
				if (position)
				{
					transform.position = Vector3.MoveTowards(
						transform.position,
						targetPosition,
						Time.deltaTime * distanceAtReceiveTime * serializationRateHz
					);
				}

				if (rotation)
				{
					transform.rotation = Quaternion.RotateTowards(
						transform.rotation,
						targetRotation,
						Time.deltaTime * angleAtReceiveTime * serializationRateHz
					);
				}
			}


			if (scale)
			{
				transform.localScale = Vector3.Lerp(
					transform.localScale,
					targetScale,
					Time.deltaTime * serializationRateHz
				);
			}
		}
	}
}