using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VelNet
{
	public class PlayerController : NetworkSerializedObject
	{
		public Vector3 targetPosition;
		public Quaternion targetRotation;


		// Update is called once per frame
		private void Update()
		{
			if (!IsMine)
			{
				transform.position = Vector3.Lerp(transform.position, targetPosition, .1f);
				transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, .1f);
			}
			else
			{
				Vector3 movement = new Vector3();
				movement.x += Input.GetAxis("Horizontal");
				movement.y += Input.GetAxis("Vertical");
				movement.z = 0;
				transform.Translate(movement * Time.deltaTime);

				if (Input.GetKeyDown(KeyCode.Space))
				{
					VelNetManager.InstantiateNetworkObject("TestNetworkedGameObject");
				}

				if (Input.GetKeyDown(KeyCode.BackQuote))
				{
					foreach (KeyValuePair<string, NetworkObject> kvp in VelNetManager.instance.objects)
					{
						Owner.TakeOwnership(kvp.Key);
					}
				}

				if (Input.GetKeyDown(KeyCode.Backspace))
				{
					foreach (KeyValuePair<string, NetworkObject> kvp in VelNetManager.instance.objects)
					{
						// don't destroy player objects
						if (!kvp.Value.ownershipLocked)
						{
							Owner.NetworkDestroy(kvp.Key);
						}
					}
				}
			}
		}


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
	}
}