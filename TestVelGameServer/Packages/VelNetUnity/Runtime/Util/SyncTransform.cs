using System;
using System.Collections;
using UnityEngine;


namespace VelNetUnity
{
	/// <summary>
	/// A simple class that will sync the position and rotation of a network object
	/// </summary>
	public class SyncTransform : NetworkObject
	{
		public Vector3 targetPosition;
		public Quaternion targetRotation;


		public byte[] GetSyncMessage()
		{
			float[] data = new float[7];
			for (int i = 0; i < 3; i++)
			{
				data[i] = transform.position[i];
				data[i + 3] = transform.rotation[i];
			}

			data[6] = transform.rotation[3];

			byte[] toReturn = new byte[sizeof(float) * data.Length];
			Buffer.BlockCopy(data, 0, toReturn, 0, toReturn.Length);
			return toReturn;
		}

		public override void HandleMessage(string identifier, byte[] message)
		{
			switch (identifier)
			{
				case "s":
					float[] data = new float[7];
					Buffer.BlockCopy(message, 0, data, 0, message.Length);
					for (int i = 0; i < 3; i++)
					{
						targetPosition[i] = data[i];
						targetRotation[i] = data[i + 3];
					}

					targetRotation[3] = data[6];
					break;
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