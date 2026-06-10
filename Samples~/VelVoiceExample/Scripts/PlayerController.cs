using UnityEngine;

namespace VelNet
{
	public class PlayerController : MonoBehaviour
	{
		public NetworkObject networkObject;

		private void Update()
		{
			if (networkObject.IsMine)
			{
				Vector3 movement = new Vector3();
				movement.x += Input.GetAxis("Horizontal");
				movement.y += Input.GetAxis("Vertical");
				movement.z = 0;
				transform.Translate(movement * Time.deltaTime);
			}
		}
	}
}