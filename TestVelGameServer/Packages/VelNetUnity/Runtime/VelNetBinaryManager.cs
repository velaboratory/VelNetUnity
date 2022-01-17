using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Net;
using UnityEngine.SceneManagement;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.IO;

namespace VelNet
{
	[AddComponentMenu("VelNet/VelNet Manager")]
	public class VelNetBinaryManager : MonoBehaviour
	{
		public enum MessageType
		{
			OTHERS = 0,
			ALL = 1,
			OTHERS_ORDERED = 2,
			ALL_ORDERED = 3
		};

		public string host;
		public int port;

		public static VelNetBinaryManager instance;

		private TcpClient socketConnection;
		private Socket udpSocket;
		public bool udpConnected;
		private IPEndPoint RemoteEndPoint;
		private Thread clientReceiveThread;
		private Thread clientReceiveThreadUDP;
		public int userid = -1;
		public string room;
		private int messagesReceived = 0;

		public readonly Dictionary<int, VelNetPlayer> players = new Dictionary<int, VelNetPlayer>();

		/// <summary>
		/// We just joined a room
		/// string - the room name
		/// </summary>
		public static Action<string> OnJoinedRoom;

		/// <summary>
		/// We just left a room
		/// string - the room name we left
		/// </summary>
		public static Action<string> OnLeftRoom;

		/// <summary>
		/// Somebody else just joined our room
		/// </summary>
		public static Action<VelNetPlayer> OnPlayerJoined;

		/// <summary>
		/// Somebody else just left our room
		/// </summary>
		public static Action<VelNetPlayer> OnPlayerLeft;

		public static Action OnConnectedToServer;
		public static Action<Message> MessageReceived;
		public static Action LoggedIn;
		public static Action<string[], int> RoomsReceived;

		public bool connected;

		public List<NetworkObject> prefabs = new List<NetworkObject>();
		public NetworkObject[] sceneObjects;
		public List<string> deletedSceneObjects = new List<string>();

		/// <summary>
		/// Maintains a list of all known objects on the server (ones that have ids)
		/// </summary>
		public readonly Dictionary<string, NetworkObject> objects = new Dictionary<string, NetworkObject>();

		/// <summary>
		/// Maintains a list of all known groups on the server
		/// </summary>
		public readonly Dictionary<string, List<int>> groups = new Dictionary<string, List<int>>();

		private VelNetPlayer masterPlayer;
		public static VelNetPlayer LocalPlayer => instance.players.Where(p => p.Value.isLocal).Select(p => p.Value).FirstOrDefault();
		public static bool InRoom => LocalPlayer != null && LocalPlayer.room != "-1" && LocalPlayer.room != "";


		// Use this for initialization
		public class Message
		{
			public int type;
			public string text;
			public int sender;
		}

		public readonly List<Message> receivedMessages = new List<Message>();

		private void Awake()
		{
			if (instance != null)
			{
				Debug.LogError("Multiple NetworkManagers detected! Bad!", this);
			}

			instance = this;

			SceneManager.sceneLoaded += (scene, mode) =>
			{
				// add all local network objects
				sceneObjects = FindObjectsOfType<NetworkObject>().Where(o => o.isSceneObject).ToArray();
			};
		}

		private IEnumerator Start()
		{
			ConnectToTcpServer();
			yield return null;

			try
			{
				OnConnectedToServer?.Invoke();
			}
			// prevent errors in subscribers from breaking our code
			catch (Exception e)
			{
				Debug.LogError(e);
			}
		}


		private void AddMessage(Message m)
		{
			lock (receivedMessages)
			{
				//Debug.Log(messagesReceived++);
				receivedMessages.Add(m);
			}
		}

		private void Update()
		{
			lock (receivedMessages)
			{
				////the main thread, which can do Unity stuff
				//foreach (Message m in receivedMessages)
				//{
				//	switch (m.type)
				//	{
				//		// when you join the server
				//		case 0:
				//			userid = m.sender;
				//			Debug.Log("joined server");

				//			try
				//			{
				//				LoggedIn?.Invoke();
				//			}
				//			// prevent errors in subscribers from breaking our code
				//			catch (Exception e)
				//			{
				//				Debug.LogError(e);
				//			}

				//			//start the udp thread 
				//			clientReceiveThreadUDP = new Thread(ListenForDataUDP);
				//			clientReceiveThreadUDP.IsBackground = true;
				//			clientReceiveThreadUDP.Start();
				//			break;
				//		// if this message is for me, that means I joined a new room...
				//		case 2 when userid == m.sender:
				//			{
				//				string oldRoom = LocalPlayer?.room;

				//				// we clear the list, but will recreate as we get messages from people in our room
				//				players.Clear();
				//				masterPlayer = null;

				//				if (m.text != "")
				//				{
				//					VelNetPlayer player = new VelNetPlayer
				//					{
				//						isLocal = true,
				//						userid = m.sender,
				//						room = m.text
				//					};

				//					players.Add(userid, player);
				//					if (m.text != "")
				//					{
				//						try
				//						{
				//							OnJoinedRoom?.Invoke(m.text);
				//						}
				//						// prevent errors in subscribers from breaking our code
				//						catch (Exception e)
				//						{
				//							Debug.LogError(e);
				//						}
				//					}
				//				}
				//				// we just left a room
				//				else
				//				{
				//					// delete all networkobjects that aren't sceneobjects or are null now
				//					objects
				//						.Where(kvp => kvp.Value == null || !kvp.Value.isSceneObject)
				//						.Select(o => o.Key)
				//						.ToList().ForEach(NetworkDestroy);

				//					// then remove references to the ones that are left
				//					objects.Clear();

				//					// empty all the groups
				//					foreach (string group in instance.groups.Keys)
				//					{
				//						SetupMessageGroup(group, new List<int>());
				//					}

				//					instance.groups.Clear();

				//					try
				//					{
				//						OnLeftRoom?.Invoke(oldRoom);
				//					}
				//					// prevent errors in subscribers from breaking our code
				//					catch (Exception e)
				//					{
				//						Debug.LogError(e);
				//					}
				//				}

				//				break;
				//			}
				//		// not for me, a player is joining or leaving
				//		case 2:
				//			{
				//				VelNetPlayer me = players[userid];

				//				if (me.room != m.text)
				//				{
				//					// we got a left message, kill it
				//					// change ownership of all objects to master
				//					List<string> deleteObjects = new List<string>();
				//					foreach (KeyValuePair<string, NetworkObject> kvp in objects)
				//					{
				//						if (kvp.Value.owner == players[m.sender]) // the owner is the player that left
				//						{
				//							// if this object has locked ownership, delete it
				//							if (kvp.Value.ownershipLocked)
				//							{
				//								deleteObjects.Add(kvp.Value.networkId);
				//							}
				//							// I'm the local master player, so can take ownership immediately
				//							else if (me.isLocal && me == masterPlayer)
				//							{
				//								TakeOwnership(kvp.Key);
				//							}
				//							// the master player left, so everyone should set the owner null (we should get a new master shortly)
				//							else if (players[m.sender] == masterPlayer)
				//							{
				//								kvp.Value.owner = null;
				//							}
				//						}
				//					}

				//					// TODO this may check for ownership in the future. We don't need ownership here
				//					deleteObjects.ForEach(NetworkDestroy);

				//					players.Remove(m.sender);
				//				}
				//				else
				//				{
				//					// we got a join message, create it
				//					VelNetPlayer player = new VelNetPlayer
				//					{
				//						isLocal = false,
				//						room = m.text,
				//						userid = m.sender
				//					};
				//					players.Add(m.sender, player);
				//					try
				//					{
				//						OnPlayerJoined?.Invoke(player);
				//					}
				//					// prevent errors in subscribers from breaking our code
				//					catch (Exception e)
				//					{
				//						Debug.LogError(e);
				//					}
				//				}

				//				break;
				//			}
				//		// generic message
				//		case 3:
				//			if (players.ContainsKey(m.sender))
				//			{
				//				players[m.sender]?.HandleMessage(m);
				//			}
				//			else
				//			{
				//				Debug.LogError("Received message from player that doesn't exist: " + m.text);
				//			}

				//			break;
				//		// change master player (this should only happen when the first player joins or if the master player leaves)
				//		case 4:
				//			{
				//				if (masterPlayer == null)
				//				{
				//					masterPlayer = players[m.sender];

				//					// no master player yet, add the scene objects

				//					for (int i = 0; i < sceneObjects.Length; i++)
				//					{
				//						sceneObjects[i].networkId = -1 + "-" + i;
				//						sceneObjects[i].owner = masterPlayer;
				//						sceneObjects[i].isSceneObject = true; // needed for special handling when deleted
				//						objects.Add(sceneObjects[i].networkId, sceneObjects[i]);
				//					}
				//				}
				//				else
				//				{
				//					masterPlayer = players[m.sender];
				//				}

				//				masterPlayer.SetAsMasterPlayer();

				//				// master player should take over any objects that do not have an owner
				//				foreach (KeyValuePair<string, NetworkObject> kvp in objects)
				//				{
				//					kvp.Value.owner ??= masterPlayer;
				//				}

				//				break;
				//			}
				//	}

				//	MessageReceived?.Invoke(m);
				//}

				//receivedMessages.Clear();
			}
		}

		private void OnApplicationQuit()
		{
			socketConnection.Close();
		}

		/// <summary> 	
		/// Setup socket connection. 	
		/// </summary> 	
		private void ConnectToTcpServer()
		{
			try
			{
				clientReceiveThread = new Thread(ListenForData);
				clientReceiveThread.IsBackground = true;
				clientReceiveThread.Start();
			}
			catch (Exception e)
			{
				Debug.Log("On client connect exception " + e);
			}
		}

		private void HandleMessage(string s) // this parses messages from the server, and adds them to a queue to be processed on the main thread
		{
			// Debug.Log("Received: " + s);
			Message m = new Message();
			string[] sections = s.Split(':');
			if (sections.Length <= 0) return;

			int type = int.Parse(sections[0]);

			switch (type)
			{
				case 0: // logged in message
					{
						if (sections.Length > 1)
						{
							m.type = type;
							m.sender = int.Parse(sections[1]);
							m.text = "";
							AddMessage(m);
						}

						break;
					}
				case 1: // room info message
					{
						break;
					}
				case 2: // joined room message
					{
						if (sections.Length > 2)
						{
							m.type = 2;
							int user_id = int.Parse(sections[1]);
							m.sender = user_id;
							string new_room = sections[2];
							m.text = new_room;

							AddMessage(m);
						}

						break;
					}
				case 3: // text message
					{
						if (sections.Length > 2)
						{
							m.type = 3;
							m.sender = int.Parse(sections[1]);
							m.text = sections[2];
							AddMessage(m);
						}

						break;
					}
				case 4: // change master client
					{
						if (sections.Length > 1)
						{
							m.type = 4;
							m.sender = int.Parse(sections[1]);
							AddMessage(m);
						}

						break;
					}
			}
		}

		/// <summary> 	
		/// Runs in background clientReceiveThread; Listens for incomming data. 	
		/// </summary>     
		private void ListenForData()
		{
			connected = true;
			try
			{
				socketConnection = new TcpClient(host, port);
				socketConnection.NoDelay = true;
				byte[] bytes = new byte[1024];
				string partialMessage = "";
				Login("Kyle", "Johnsen");
				while (true)
				{
					
					// Get a stream object for reading 				
					using NetworkStream stream = socketConnection.GetStream();
					int length;
					// Read incomming stream into byte arrary. 					
					while ((length = stream.Read(bytes, 0, bytes.Length)) != 0)
					{
						Debug.Log("read " + length + " bytes!");
						//byte[] incommingData = new byte[length];
						//Array.Copy(bytes, 0, incommingData, 0, length);
						//// Convert byte array to string message. 						
						//string serverMessage = Encoding.ASCII.GetString(incommingData);
						//string[] sections = serverMessage.Split('\n');
						//if (sections.Length > 1)
						//{
						//	lock (receivedMessages)
						//	{
						//		for (int i = 0; i < sections.Length - 1; i++)
						//		{
						//			if (i == 0)
						//			{
						//				HandleMessage(partialMessage + sections[0]);
						//				partialMessage = "";
						//			}
						//			else
						//			{
						//				HandleMessage(sections[i]);
						//			}
						//		}
						//	}
						//}

						//partialMessage = partialMessage + sections[sections.Length - 1];
					}
				}
			}


			catch (Exception socketException)
			{
				Debug.Log("Socket exception: " + socketException);
			}

			connected = false;
		}

		private void ListenForDataUDP()
		{
			//I don't yet have a UDP connection
			try
			{
				IPAddress[] addresses = Dns.GetHostAddresses(host);
				Debug.Assert(addresses.Length > 0);
				RemoteEndPoint = new IPEndPoint(addresses[0], port);


				udpSocket = new Socket(AddressFamily.InterNetwork,
					SocketType.Dgram, ProtocolType.Udp);


				udpConnected = false;
				byte[] buffer = new byte[1024];
				while (true)
				{
					string welcome = userid + ":0:Hello";
					byte[] data = Encoding.ASCII.GetBytes(welcome);
					udpSocket.SendTo(data, data.Length, SocketFlags.None, RemoteEndPoint);

					if (udpSocket.Available == 0)
					{
						Thread.Sleep(100);
						Debug.Log("Waiting for UDP response");
					}
					else
					{
						break;
					}
				}

				udpConnected = true;
				while (true)
				{
					int numReceived = udpSocket.Receive(buffer);

					string message = Encoding.UTF8.GetString(buffer, 0, numReceived);

					string[] sections = message.Split(':');
					if (sections[0] == "0")
					{
						Debug.Log("UDP connected");
					}

					if (sections[0] == "3")
					{
						HandleMessage(message);
					}
				}
			}
			catch (Exception socketException)
			{
				Debug.Log("Socket exception: " + socketException);
			}
		}

		private static void SendUdpMessage(string message)
		{
			if (instance.udpSocket == null || !instance.udpConnected)
			{
				return;
			}

			byte[] data = Encoding.UTF8.GetBytes(message);
			//Debug.Log("Attempting to send: " + message);
			instance.udpSocket.SendTo(data, data.Length, SocketFlags.None, instance.RemoteEndPoint);
		}

		/// <summary> 	
		/// Send message to server using socket connection. 	
		/// </summary> 	
		private static void SendNetworkMessage(byte[] message)
		{
			// Debug.Log("Sent: " + clientMessage);
			if (instance.socketConnection == null)
			{
				return;
			}

			try
			{
				// Get a stream object for writing. 			
				NetworkStream stream = instance.socketConnection.GetStream();
				if (stream.CanWrite)
				{
					
					stream.Write(message,0,message.Length);
				}
			}
			catch (SocketException socketException)
			{
				Debug.Log("Socket exception: " + socketException);
			}
		}

		/// <summary>
		/// Connects to the server with a username
		/// </summary>
		/// 
		public static byte[] get_be_bytes(int n)
		{
			return BitConverter.GetBytes(n).Reverse().ToArray();
		}
		public static void Login(string username, string password)
		{
			
			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);
			
			byte[] uB = Encoding.UTF8.GetBytes(username);
			byte[] uP = Encoding.UTF8.GetBytes(password);
			writer.Write((byte)0);
			writer.Write(get_be_bytes(uB.Length));
			writer.Write(uB);
			writer.Write(get_be_bytes(uP.Length));
			writer.Write(uP);

			SendNetworkMessage(stream.ToArray());
			Join("MyRoom");
		}

		/// <summary>
		/// Joins a room by name
		/// </summary>
		/// <param name="roomname">The name of the room to join</param>
		public static void Join(string roomname)
		{
			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);

			byte[] R = Encoding.UTF8.GetBytes(roomname);
			writer.Write((byte)2);
			writer.Write(get_be_bytes(R.Length));
			writer.Write(R);
			SendNetworkMessage(stream.ToArray());
		}

		/// <summary>
		/// Leaves a room if we're in one
		/// </summary>
		public static void Leave()
		{
			//if (InRoom) SendNetworkMessage("2:-1");
		}

		public static void SendTo(MessageType type, string message, bool reliable = true)
		{
			if (reliable)
			{
				//SendNetworkMessage("3:" + (int)type + ":" + message);
			}
			else
			{
				SendUdpMessage(instance.userid + ":3:" + (int)type + ":" + message);
			}
		}

		public static void SendToGroup(string group, string message, bool reliable = true)
		{
			if (reliable)
			{
				//SendNetworkMessage("4:" + group + ":" + message);
			}
			else
			{
				SendUdpMessage(instance.userid + ":4:" + group + ":" + message);
			}
		}

		/// <summary>
		/// changes the designated group that sendto(4) will go to
		/// </summary>
		public static void SetupMessageGroup(string groupName, List<int> userIds)
		{
			if (userIds.Count > 0)
			{
				instance.groups[groupName] = userIds.ToList();
			}

			//SendNetworkMessage($"5:{groupName}:{string.Join(":", userIds)}");
		}


		public static NetworkObject InstantiateNetworkObject(string prefabName)
		{
			VelNetPlayer localPlayer = LocalPlayer;
			NetworkObject prefab = instance.prefabs.Find(p => p.name == prefabName);
			if (prefab == null)
			{
				Debug.LogError("Couldn't find a prefab with that name: " + prefabName);
				return null;
			}

			string networkId = localPlayer.userid + "-" + localPlayer.lastObjectId++;
			if (instance.objects.ContainsKey(networkId))
			{
				Debug.LogError("Can't instantiate object. Obj with that network ID was already instantiated.", instance.objects[networkId]);
				return null;
			}
			NetworkObject newObject = Instantiate(prefab);
			newObject.networkId = networkId;
			newObject.prefabName = prefabName;
			newObject.owner = localPlayer;
			instance.objects.Add(newObject.networkId, newObject);

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			SendTo(MessageType.OTHERS, "7," + newObject.networkId + "," + prefabName);

			return newObject;
		}

		public static void SomebodyInstantiatedNetworkObject(string networkId, string prefabName, VelNetPlayer owner)
		{
			NetworkObject prefab = instance.prefabs.Find(p => p.name == prefabName);
			if (prefab == null) return;
			NetworkObject newObject = Instantiate(prefab);
			newObject.networkId = networkId;
			newObject.prefabName = prefabName;
			newObject.owner = owner;
			instance.objects.Add(newObject.networkId, newObject);
		}

		public static void NetworkDestroy(NetworkObject obj)
		{
			NetworkDestroy(obj.networkId);
		}

		public static void NetworkDestroy(string networkId)
		{
			if (!instance.objects.ContainsKey(networkId)) return;
			NetworkObject obj = instance.objects[networkId];
			if (obj == null)
			{
				instance.objects.Remove(networkId);
				return;
			}
			if (obj.isSceneObject)
			{
				instance.deletedSceneObjects.Add(networkId);
			}

			Destroy(obj.gameObject);
			instance.objects.Remove(networkId);
		}

		/// <summary>
		/// Takes local ownership of an object by id.
		/// </summary>
		/// <param name="networkId">Network ID of the object to transfer</param>
		/// <returns>True if successfully transferred, False if transfer message not sent</returns>
		public static bool TakeOwnership(string networkId)
		{
			// local player must exist
			if (LocalPlayer == null) return false;

			// obj must exist
			if (!instance.objects.ContainsKey(networkId)) return false;

			// if the ownership is locked, fail
			if (instance.objects[networkId].ownershipLocked) return false;

			// immediately successful
			instance.objects[networkId].owner = LocalPlayer;

			// must be ordered, so that ownership transfers are not confused.  Also sent to all players, so that multiple simultaneous requests will result in the same outcome.
			SendTo(MessageType.ALL_ORDERED, "6," + networkId);

			return true;
		}
	}
}