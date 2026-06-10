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
		public bool alwaysTeleport;
		public bool rotation = true;
		[Tooltip("Scale is always local")] public bool scale;

		[Space] public bool useLocalTransform;

		[Tooltip("0 to disable.")] public float teleportDistance;
		[Tooltip("0 to disable.")] public float teleportAngle;

		[Tooltip("Quantize SENT positions to this step in meters (0 = off). Sub-step sensor " +
		         "jitter (a worn-but-still headset moves fractions of a mm every tick) then " +
		         "serializes byte-identically, so on-change compression suppresses the send — " +
		         "an idle player drops from ~30 sends/s to a trickle. 0.5mm is below tracking " +
		         "noise, so motion looks identical. Same wire format (plain floats).")]
		public float positionQuantum = 0.0005f;
		[Tooltip("Quantize SENT rotation quaternion components to this step (0 = off). " +
		         "0.001 ≈ 0.1 degree — below perceptible/tracking noise.")]
		public float rotationQuantum = 0.001f;

		private Vector3 targetScale;
		private Vector3 targetPosition;
		private Quaternion targetRotation;
		private float distanceAtReceiveTime;
		private float angleAtReceiveTime;
		// True once the transform has reached the last received target. Lets Update()
		// skip ALL transform access for parked objects — with hundreds of synced objects
		// in a room, the per-frame MoveTowards/property calls were real main-thread time
		// on Quest. Starts true (Start() snaps the targets to the current transform).
		private bool _settled = true;



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
		protected override void SendState(NetworkWriter writer)
		{
			if (useLocalTransform)
			{
				if (position) writer.Write(Quantize(transform.localPosition, positionQuantum));
				if (rotation) writer.Write(Quantize(transform.localRotation, rotationQuantum));
			}
			else
			{
				if (position) writer.Write(Quantize(transform.position, positionQuantum));
				if (rotation) writer.Write(Quantize(transform.rotation, rotationQuantum));
			}

			if (scale) writer.Write(Quantize(transform.localScale, positionQuantum));
		}

		private static Vector3 Quantize(Vector3 v, float q)
		{
			if (q <= 0) return v;
			return new Vector3(
				Mathf.Round(v.x / q) * q,
				Mathf.Round(v.y / q) * q,
				Mathf.Round(v.z / q) * q);
		}

		private static Quaternion Quantize(Quaternion r, float q)
		{
			if (q <= 0) return r;
			// Component-wise rounding leaves the quaternion ≤q from unit length —
			// receivers' RotateTowards/assignment tolerate that comfortably at 1e-3.
			return new Quaternion(
				Mathf.Round(r.x / q) * q,
				Mathf.Round(r.y / q) * q,
				Mathf.Round(r.z / q) * q,
				Mathf.Round(r.w / q) * q);
		}

		/// <summary>
		/// This gets called whenever a message about the state of this object is received.
		/// Usually at serializationRateHz.
		/// </summary>
		protected override void ReceiveState(NetworkReader reader)
		{
			if (position) targetPosition = reader.ReadVector3();
			if (rotation) targetRotation = reader.ReadQuaternion();
			if (scale) targetScale = reader.ReadVector3();

			// record the distance from the target for interpolation
			if (useLocalTransform)
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.localPosition);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.localRotation);
				if (alwaysTeleport || IsMine || receivingPackedState || teleportDistance != 0 && teleportDistance < distanceAtReceiveTime)
				{
					transform.localPosition = targetPosition;
				}

				if (alwaysTeleport || IsMine || receivingPackedState || teleportAngle != 0 && teleportAngle < angleAtReceiveTime)
				{
					transform.localRotation = targetRotation;
				}
			}
			else
			{
				distanceAtReceiveTime = Vector3.Distance(targetPosition, transform.position);
				angleAtReceiveTime = Quaternion.Angle(targetRotation, transform.rotation);
				if (alwaysTeleport || IsMine || receivingPackedState || teleportDistance != 0 && teleportDistance < distanceAtReceiveTime)
				{
					transform.position = targetPosition;
				}

				if (alwaysTeleport || IsMine || receivingPackedState || teleportAngle != 0 && teleportAngle < angleAtReceiveTime)
				{
					transform.rotation = targetRotation;
				}
			}
			if(scale) transform.localScale = targetScale;
			_settled = false; // fresh targets — resume interpolating
		}

		private void Update()
		{
			if (IsMine) return;
			if (_settled) return; // at target — no transform work until new state arrives

			bool done = true;

			if (useLocalTransform)
			{
				if (position)
				{
					transform.localPosition = Vector3.MoveTowards(
						transform.localPosition,
						targetPosition,
						Time.deltaTime * distanceAtReceiveTime * serializationRateHz
					);
					if (transform.localPosition != targetPosition) done = false;
				}

				if (rotation)
				{
					transform.localRotation = Quaternion.RotateTowards(
						transform.localRotation,
						targetRotation,
						Time.deltaTime * angleAtReceiveTime * serializationRateHz
					);
					if (Quaternion.Angle(transform.localRotation, targetRotation) > 0.01f) done = false;
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
					if (transform.position != targetPosition) done = false;
				}

				if (rotation)
				{
					transform.rotation = Quaternion.RotateTowards(
						transform.rotation,
						targetRotation,
						Time.deltaTime * angleAtReceiveTime * serializationRateHz
					);
					if (Quaternion.Angle(transform.rotation, targetRotation) > 0.01f) done = false;
				}
			}


			if (scale)
			{
				// Lerp approaches asymptotically — snap once close so we can settle.
				Vector3 ls = Vector3.Lerp(
					transform.localScale,
					targetScale,
					Time.deltaTime * serializationRateHz
				);
				if ((ls - targetScale).sqrMagnitude < 1e-8f) ls = targetScale;
				else done = false;
				transform.localScale = ls;
			}

			_settled = done;
		}
	}
}
