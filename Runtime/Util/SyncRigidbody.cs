using UnityEngine;

namespace VelNet
{
	/// <summary>
	/// A simple class that will sync the position and rotation of a network object with a rigidbody
	/// This only uses the rigidbody for interpolation - it doesn't do any interpolatin itself
	/// </summary>
	[AddComponentMenu("VelNet/VelNet Sync Rigidbody")]
	[RequireComponent(typeof(Rigidbody))]
	public class SyncRigidbody : SyncState
	{
		public bool useLocalTransform;
		public float minPosDelta = .01f;
		public float minAngleDelta = .1f;
		public float minVelDelta = .01f;
		public float minAngVelDelta = .1f;

		public bool syncKinematic = true;
		public bool syncGravity = true;
		public bool syncVelocity = true;
		public bool syncAngularVelocity = true;

		private Vector3 targetPosition;
		private Quaternion targetRotation;
		private Vector3 targetVel;
		private Vector3 targetAngVel;
		private float distanceAtReceiveTime;
		private float angleAtReceiveTime;
		private Rigidbody rb;

		protected override void Awake()
		{
			base.Awake();
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
		protected override void SendState(NetworkWriter writer)
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

			if (syncVelocity) writer.Write(rb.linearVelocity);
			if (syncAngularVelocity) writer.Write(rb.angularVelocity);

			if (syncKinematic) writer.Write(rb.isKinematic);
			if (syncGravity) writer.Write(rb.useGravity);
		}

		/// <summary>
		/// This gets called whenever a message about the state of this object is received.
		/// Usually at serializationRateHz.
		/// </summary>
		protected override void ReceiveState(NetworkReader reader)
		{
			targetPosition = reader.ReadVector3();
			targetRotation = reader.ReadQuaternion();

			if (syncVelocity) targetVel = reader.ReadVector3();
			if (syncAngularVelocity) targetAngVel = reader.ReadVector3();

			if (syncKinematic) rb.isKinematic = reader.ReadBool();
			if (syncGravity) rb.useGravity = reader.ReadBool();

			// record the distance from the target for interpolation
			if (useLocalTransform)
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.localPosition);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.localRotation);
				if (IsMine || minPosDelta < distanceAtReceiveTime)
				{
					transform.localPosition = targetPosition;
				}

				if (IsMine || minAngleDelta < angleAtReceiveTime)
				{
					transform.localRotation = targetRotation;
				}
			}
			else
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.position);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.rotation);
				if (IsMine || minPosDelta < distanceAtReceiveTime)
				{
					transform.position = targetPosition;
				}

				if (IsMine || minAngleDelta < angleAtReceiveTime)
				{
					transform.rotation = targetRotation;
				}
			}

			float velDelta = Vector3.Distance(targetVel, rb.linearVelocity);
			float angVelDelta = Vector3.Distance(targetAngVel, rb.angularVelocity);
			if (velDelta > minVelDelta)
			{
				rb.linearVelocity = targetVel;
			}

			if (angVelDelta > minAngVelDelta)
			{
				rb.angularVelocity = targetAngVel;
			}
		}
	}
}
