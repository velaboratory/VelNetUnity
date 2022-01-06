using System.Collections;
using System.IO;
using UnityEngine;


namespace VelNetUnity
{
	/// <summary>
	/// A simple class that will sync the position and rotation of a network object
	/// </summary>
	[AddComponentMenu("VelNetUnity/VelNet Sync Transform")]
	public class SyncTransform : NetworkObject
	{
		public Vector3 targetPosition;
		public Quaternion targetRotation;


		public byte[] GetSyncMessage()
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);

			writer.Write(transform.position);
			writer.Write(transform.rotation);

			return mem.ToArray();
		}

		public override void HandleMessage(string identifier, byte[] message)
		{
			switch (identifier)
			{
				case "s":
				{
					using MemoryStream mem = new MemoryStream(message);
					using BinaryReader reader = new BinaryReader(mem);

					targetPosition = reader.ReadVector3();
					targetRotation = reader.ReadQuaternion();

					break;
				}
			}
		}

		// Start is called before the first frame update
		private void Start()
		{
			StartCoroutine(SyncBehavior());
		}

		private IEnumerator SyncBehavior()
		{
			while (true)
			{
				if (owner != null && owner.isLocal)
				{
					owner.SendMessage(this, "s", GetSyncMessage());
				}

				yield return new WaitForSeconds(.1f);
			}
		}

		// Update is called once per frame
		private void Update()
		{
			if (owner != null && !owner.isLocal)
			{
				transform.position = Vector3.Lerp(transform.position, targetPosition, .1f);
				transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, .1f);
			}
		}
	}
}