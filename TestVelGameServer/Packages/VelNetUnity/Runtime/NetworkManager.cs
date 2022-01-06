using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Net;

namespace VelNetUnity
{
	[AddComponentMenu("VelNetUnity/VelNet Network Manager")]
	public class NetworkManager : MonoBehaviour
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

		public static NetworkManager instance;

		#region private members

		private TcpClient socketConnection;
		private Socket udpSocket;
		public bool udpConnected;
		private IPEndPoint RemoteEndPoint;
		private Thread clientReceiveThread;
		private Thread clientReceiveThreadUDP;
		public int userid = -1;
		public string room;
		private int messagesReceived = 0;

		public GameObject playerPrefab;
		public Dictionary<int, NetworkPlayer> players = new Dictionary<int, NetworkPlayer>();

		public Action<NetworkPlayer> OnJoinedRoom;
		public Action<NetworkPlayer> OnPlayerJoined;
		public Action<NetworkPlayer> OnPlayerLeft;

		public List<NetworkObject> prefabs = new List<NetworkObject>();
		public NetworkObject[] sceneObjects;
		public List<string> deletedSceneObjects = new List<string>();
		public readonly Dictionary<string, NetworkObject> objects = new Dictionary<string, NetworkObject>(); //maintains a list of all known objects on the server (ones that have ids)
		private NetworkPlayer masterPlayer;

		#endregion

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
		}

		private void Start()
		{
			ConnectToTcpServer();
			sceneObjects = FindObjectsOfType<NetworkObject>(); //add all local network objects
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
				//the main thread, which can do Unity stuff
				foreach (Message m in receivedMessages)
				{
					if (m.type == 0) //when you join the server
					{
						userid = m.sender;
						Debug.Log("joined server");

						//start the udp thread 
						clientReceiveThreadUDP = new Thread(ListenForDataUDP);
						clientReceiveThreadUDP.IsBackground = true;
						clientReceiveThreadUDP.Start();
					}

					if (m.type == 2)
					{
						//if this message is for me, that means I joined a new room...
						if (userid == m.sender)
						{
							foreach (KeyValuePair<int, NetworkPlayer> kvp in players)
							{
								Destroy(kvp.Value.gameObject);
							}

							players.Clear(); //we clear the list, but will recreate as we get messages from people in our room

							if (m.text != "")
							{
								NetworkPlayer player = Instantiate(playerPrefab).GetComponent<NetworkPlayer>();

								player.isLocal = true;
								player.userid = m.sender;
								players.Add(userid, player);
								player.room = m.text;
								OnJoinedRoom?.Invoke(player);
							}
						}
						else //not for me, a player is joining or leaving
						{
							NetworkPlayer me = players[userid];

							if (me.room != m.text)
							{
								//we got a left message, kill it
								//change ownership of all objects to master

								foreach (KeyValuePair<string, NetworkObject> kvp in objects)
								{
									if (kvp.Value.owner == players[m.sender]) //the owner is the player that left
									{
										if (me.isLocal && me == masterPlayer) //I'm the local master player, so can take ownership immediately
										{
											me.TakeOwnership(kvp.Key);
										}
										else if (players[m.sender] == masterPlayer) //the master player left, so everyone should set the owner null (we should get a new master shortly)
										{
											kvp.Value.owner = null;
										}
									}
								}

								Destroy(players[m.sender].gameObject);
								players.Remove(m.sender);
							}
							else
							{
								//we got a join mesage, create it
								NetworkPlayer player = Instantiate(playerPrefab).GetComponent<NetworkPlayer>();
								player.isLocal = false;
								player.room = m.text;
								player.userid = m.sender;
								players.Add(m.sender, player);
								OnPlayerJoined?.Invoke(player);
							}
						}
					}

					if (m.type == 3) //generic message
					{
						players[m.sender]?.HandleMessage(m);
					}

					if (m.type == 4) //change master player (this should only happen when the first player joins or if the master player leaves)
					{
						if (masterPlayer == null)
						{
							masterPlayer = players[m.sender];

							//no master player yet, add the scene objects

							for (int i = 0; i < sceneObjects.Length; i++)
							{
								sceneObjects[i].networkId = -1 + "-" + i;
								sceneObjects[i].owner = masterPlayer;
								sceneObjects[i].isSceneObject = true; //needed for special handling when deleted
								objects.Add(sceneObjects[i].networkId, sceneObjects[i]);
							}
						}
						else
						{
							masterPlayer = players[m.sender];
						}

						masterPlayer.SetAsMasterPlayer();

						//master player should take over any objects that do not have an owner

						foreach (KeyValuePair<string, NetworkObject> kvp in objects)
						{
							if (kvp.Value.owner == null)
							{
								kvp.Value.owner = masterPlayer;
							}
						}
					}

					MessageReceived(m);
				}

				receivedMessages.Clear();
			}
		}

		private void OnApplicationQuit()
		{
			socketConnection.Close();
		}

		public Action<string, int> JoinedRoom = delegate { };
		public Action<Message> MessageReceived = delegate { };
		public Action<string, int> LoggedIn = delegate { };
		public Action<string[], int> RoomsReceived = delegate { };

		public bool connected;

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

		private void HandleMessage(string s) //this parses messages from the server, and adds them to a queue to be processed on the main thread
		{
			Message m = new Message();
			string[] sections = s.Split(':');
			if (sections.Length > 0)
			{
				int type = int.Parse(sections[0]);

				switch (type)
				{
					case 0: //logged in message
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
					case 1: //room info message
					{
						break;
					}
					case 2: //joined room message
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
					case 3: //text message
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
					case 4: //change master client
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
				while (true)
				{
					// Get a stream object for reading 				
					using (NetworkStream stream = socketConnection.GetStream())
					{
						int length;
						// Read incomming stream into byte arrary. 					
						while ((length = stream.Read(bytes, 0, bytes.Length)) != 0)
						{
							byte[] incommingData = new byte[length];
							Array.Copy(bytes, 0, incommingData, 0, length);
							// Convert byte array to string message. 						
							string serverMessage = Encoding.ASCII.GetString(incommingData);
							string[] sections = serverMessage.Split('\n');
							if (sections.Length > 1)
							{
								lock (receivedMessages)
								{
									for (int i = 0; i < sections.Length - 1; i++)
									{
										if (i == 0)
										{
											HandleMessage(partialMessage + sections[0]);
											partialMessage = "";
										}
										else
										{
											HandleMessage(sections[i]);
										}
									}
								}
							}

							partialMessage = partialMessage + sections[sections.Length - 1];
						}
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

		private void SendUdpMessage(string message)
		{
			if (udpSocket == null || !udpConnected)
			{
				return;
			}

			byte[] data = Encoding.UTF8.GetBytes(message);
			//Debug.Log("Attempting to send: " + message);
			udpSocket.SendTo(data, data.Length, SocketFlags.None, RemoteEndPoint);
		}

		/// <summary> 	
		/// Send message to server using socket connection. 	
		/// </summary> 	
		private void SendNetworkMessage(string clientMessage)
		{
			if (socketConnection == null)
			{
				return;
			}

			try
			{
				// Get a stream object for writing. 			
				NetworkStream stream = socketConnection.GetStream();
				if (stream.CanWrite)
				{
					// Convert string message to byte array.
					clientMessage = clientMessage + "\n"; //append a new line to delineate the message
					byte[] clientMessageAsByteArray = Encoding.ASCII.GetBytes(clientMessage);
					// Write byte array to socketConnection stream.                 
					stream.Write(clientMessageAsByteArray, 0, clientMessageAsByteArray.Length);
				}
			}
			catch (SocketException socketException)
			{
				Debug.Log("Socket exception: " + socketException);
			}
		}

		public void Login(string username, string password)
		{
			SendNetworkMessage("0:" + username + ":" + password);
		}

		public void Join(string roomname)
		{
			SendNetworkMessage("2:" + roomname);
		}

		public void Leave()
		{
			SendNetworkMessage("2:-1");
		}

		public void SendTo(MessageType type, string message, bool reliable = true)
		{
			if (reliable)
			{
				SendNetworkMessage("3:" + (int)type + ":" + message);
			}
			else
			{
				SendUdpMessage(userid + ":3:" + (int)type + ":" + message);
			}
		}

		public void SendToGroup(string group, string message, bool reliable = true)
		{
			if (reliable)
			{
				SendNetworkMessage("4:" + group + ":" + message);
			}
			else
			{
				SendUdpMessage(userid + ":4:" + group + ":" + message);
			}
		}

		/// <summary>
		/// changes the designated group that sendto(4) will go to
		/// </summary>
		public void SetupMessageGroup(string groupName, IEnumerable<int> userIds)
		{
			SendNetworkMessage($"5:{groupName}:{string.Join(":", userIds)}");
		}

		public void DeleteNetworkObject(string networkId)
		{
			if (objects.ContainsKey(networkId))
			{
				NetworkObject obj = objects[networkId];
				if (obj.isSceneObject)
				{
					deletedSceneObjects.Add(networkId);
				}

				Destroy(obj.gameObject);
				objects.Remove(networkId);
			}
		}
	}
}