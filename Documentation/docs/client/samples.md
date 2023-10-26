# Samples

This page has a brief explanation of each of the samples included in VelNetUnity. To install a sample, go to the Package Manager, and use the Import buttons in the Samples tab on the VelNet package (`Window->Package Manager->VelNet->Samples`).

## Basic Example
This sample is designed to be as minimal as possible. It automatically joins a room and spawns a controllable (WASD) player character for every client that joins.

This example contains only two scripts:

### `BasicVelNetMan.cs`

BasicVelNetMan is the network manager for this application, and it sets up two callbacks.
```cs
public class BasicVelNetMan : MonoBehaviour
{
	public GameObject playerPrefab;

	private void Start()
	{
		// join a hardcoded VelNet room as soon as we log into the server
		VelNetManager.OnLoggedIn += () => VelNetManager.JoinRoom("BasicExample");
		// then once we join the room, spawn our player prefab on the network
		VelNetManager.OnJoinedRoom += _ => { VelNetManager.NetworkInstantiate(playerPrefab.name); };
	}
}
```

### `PlayerController.cs`

PlayerController sets up very basic directional movement. The important thing to note here is that it only moves the player if this is a local network object (it is our own player controller). Since the player prefab is spawned on every client's game instance, we don't want to move all of the player controllers in the scene.

```cs
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
```

## Full Example

The Full Example is not significantly more complex than the basic example, but it contains a lot more examples for different types of syncing or interactions you may want to include.

It contains example implementations for:
 - [Custom messages](/client/reference/custom-messages)
 - [RPCs](/client/reference/rpc)
 - Inheriting from [SyncState](/client/reference/syncstate)
 - [Instantiating Objects](/client/concepts/instantiating)

## VEL Voice Example

## Dissonance Example
