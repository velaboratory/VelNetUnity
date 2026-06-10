using UnityEngine;
using VelNet;
using VelNet.Voice;

public class GameManager : MonoBehaviour
{
	public GameObject playerPrefab;
	public VelVoice velVoice;

	private void Start()
	{
		VelNetManager.OnLoggedIn += () => VelNetManager.JoinRoom("BasicExample");
		VelNetManager.OnJoinedRoom += _ => { VelNetManager.NetworkInstantiate(playerPrefab.name); };
	}
}