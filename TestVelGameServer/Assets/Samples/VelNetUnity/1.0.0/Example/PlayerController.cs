using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VelNetUnity
{
	public class PlayerController : NetworkObject
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
				case "s": // sync message
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
			// player controller shouldn't change ownership, so we can check here once
			if (owner.isLocal)
			{
				StartCoroutine(SyncBehavior());
			}
		}


		private IEnumerator SyncBehavior()
		{
			while (true)
			{
				owner.SendMessage(this, "s", GetSyncMessage());
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
			else if (owner != null && owner.isLocal)
			{
				Vector3 movement = new Vector3();
				movement.x += Input.GetAxis("Horizontal");
				movement.y += Input.GetAxis("Vertical");
				movement.z = 0;
				transform.Translate(movement * Time.deltaTime);

				if (Input.GetKeyDown(KeyCode.Space))
				{
					owner.NetworkInstantiate("TestNetworkedGameObject");
				}

				if (Input.GetKeyDown(KeyCode.BackQuote))
				{
					foreach (KeyValuePair<string, NetworkObject> kvp in NetworkManager.instance.objects)
					{
						owner.TakeOwnership(kvp.Key);
					}
				}

				if (Input.GetKeyDown(KeyCode.Backspace))
				{
					foreach (KeyValuePair<string, NetworkObject> kvp in NetworkManager.instance.objects)
					{
						owner.NetworkDestroy(kvp.Key);
					}
				}
			}
		}
	}
}