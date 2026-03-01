using System.Collections;
using UnityEngine;
using VelNet;

public class SyncedCustomObj : SyncState
{
	private Renderer rend;
	private Rigidbody rb;
	public float impulseForce = 1;
	public float returningForce = 1;

	private IEnumerator Start()
	{
		rend = GetComponent<Renderer>();
		rb = GetComponent<Rigidbody>();

		while (true)
		{
			if (IsMine)
			{
				rb.AddForce(new Vector3(
					Random.Range(-1f, 1f) * impulseForce,
					Random.Range(-1f, 1f) * impulseForce,
					Random.Range(-1f, 1f) * impulseForce
				));
			}

			yield return new WaitForSeconds(Random.Range(1f, 3f));
		}
	}

	private void FixedUpdate()
	{
		if (IsMine)
		{
			// return to center
			rb.linearVelocity -= (transform.position).normalized * Time.fixedDeltaTime * returningForce;
		}
	}

	protected override void SendState(NetworkWriter writer)
	{
	}

	protected override void ReceiveState(NetworkReader reader)
	{
	}
}
