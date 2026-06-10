using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace VelNet
{
	public class PlayerController : SyncState
	{
		private Renderer rend;
		public Color color;

		protected override void Awake()
		{
			base.Awake();

			rend = GetComponent<MeshRenderer>();
			if (IsMine)
			{
				color = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f));
				rend.material.color = color;
			}
		}

		// Update is called once per frame
		private void Update()
		{
			if (IsMine)
			{
				Vector3 movement = new Vector3();
				movement.x += Input.GetAxis("Horizontal");
				movement.y += Input.GetAxis("Vertical");
				movement.z = 0;
				transform.Translate(movement * Time.deltaTime);

				if (Input.GetKeyDown(KeyCode.Space))
				{
					VelNetManager.NetworkInstantiate("TestNetworkedGameObject");
				}

				if (Input.GetKeyDown(KeyCode.BackQuote))
				{
					foreach (KeyValuePair<long, NetworkObject> kvp in VelNetManager.instance.objects)
					{
						kvp.Value.TakeOwnership();
					}
				}

				if (Input.GetKeyDown(KeyCode.Backspace))
				{
					foreach (long key in VelNetManager.instance.objects
						         .Where(kvp => !kvp.Value.ownershipLocked)
						         .Select(kvp => kvp.Key).ToArray())
					{
						VelNetManager.NetworkDestroy(key);
					}
				}
			}
		}

		protected override void SendState(NetworkWriter writer)
		{
			writer.Write(color);
		}

		protected override void ReceiveState(NetworkReader reader)
		{
			Color newColor = reader.ReadColor();
			if (newColor != color)
			{
				rend.material.color = newColor;
			}

			color = newColor;
		}
	}
}
