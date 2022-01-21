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
	public class VelNetManager : MonoBehaviour
	{
		public enum MessageSendType
		{
			MESSAGE_OTHERS_ORDERED = 7,
			MESSAGE_ALL_ORDERED = 8,
			MESSAGE_LOGIN = 0,
			MESSAGE_GETROOMS = 1,
			MESSAGE_JOINROOM = 2,
			MESSAGE_OTHERS = 3,
			MESSAGE_ALL = 4,
			MESSAGE_GROUP = 5,
			MESSAGE_SETGROUP = 6
		};

		public string host;
		public int port;

		public static VelNetManager instance;

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


		//this is for sending udp packets
		static byte[] toSend = new byte[1024];

		// Use this for initialization
		public abstract class Message
		{
			
		}
		public class ListedRoom
		{
			public string name;
			public int numUsers;
		}
		public class LoginMessage: Message
		{
			public int userId;
		}
		public class RoomsMessage: Message
		{
			public List<ListedRoom> rooms;
		}
		public class JoinMessage: Message
		{
			public int userId;
			public string room;
		}
		public class DataMessage: Message
		{
			public int senderId;
			public byte[] data;
		}
		public class ChangeMasterMessage: Message
		{
			public int masterId;
		}

		public class ConnectedMessage: Message
		{
			
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

		private void Start()
		{
			ConnectToTcpServer();
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
					switch (m)
					{
						case ConnectedMessage connected:
							{
								try
								{
									OnConnectedToServer?.Invoke();
								}
								// prevent errors in subscribers from breaking our code
								catch (Exception e)
								{
									Debug.LogError(e);
								}
								break;
							}
						case LoginMessage lm:
							{
								userid = lm.userId;
								Debug.Log("joined server " + userid);

								try
								{
									LoggedIn?.Invoke();
								}
								// prevent errors in subscribers from breaking our code
								catch (Exception e)
								{
									Debug.LogError(e);
								}

								//start the udp thread 
								clientReceiveThreadUDP = new Thread(ListenForDataUDP);
								clientReceiveThreadUDP.IsBackground = true;
								clientReceiveThreadUDP.Start();

								break;
							}
						case RoomsMessage rm: {
								Debug.Log("Got Rooms Message");
								
								break; 
							}
						case JoinMessage jm: { 
								if(userid == jm.userId) //this is us
								{
									string oldRoom = LocalPlayer?.room;

									// we clear the list, but will recreate as we get messages from people in our room
									players.Clear();
									masterPlayer = null;

									if (jm.room != "")
									{
										VelNetPlayer player = new VelNetPlayer
										{
											isLocal = true,
											userid = jm.userId,
											room = jm.room
										};

										players.Add(userid, player);
										
										try
										{
											OnJoinedRoom?.Invoke(jm.room);
										}
										// prevent errors in subscribers from breaking our code
										catch (Exception e)
										{
											Debug.LogError(e);
										}
										
									}
									// we just left a room
									else
									{
										// delete all networkobjects that aren't sceneobjects or are null now
										objects
											.Where(kvp => kvp.Value == null || !kvp.Value.isSceneObject)
											.Select(o => o.Key)
											.ToList().ForEach(NetworkDestroy);

										// then remove references to the ones that are left
										objects.Clear();

										// empty all the groups
										foreach (string group in instance.groups.Keys)
										{
											SetupMessageGroup(group, new List<int>());
										}

										instance.groups.Clear();

										try
										{
											OnLeftRoom?.Invoke(oldRoom);
										}
										// prevent errors in subscribers from breaking our code
										catch (Exception e)
										{
											Debug.LogError(e);
										}
									}
								}
								else
								{
									VelNetPlayer me = players[userid];

									if (me.room != jm.room)
									{
										// we got a left message, kill it
										// change ownership of all objects to master
										List<string> deleteObjects = new List<string>();
										foreach (KeyValuePair<string, NetworkObject> kvp in objects)
										{
											if (kvp.Value.owner == players[jm.userId]) // the owner is the player that left
											{
												// if this object has locked ownership, delete it
												if (kvp.Value.ownershipLocked)
												{
													deleteObjects.Add(kvp.Value.networkId);
												}
												// I'm the local master player, so can take ownership immediately
												else if (me.isLocal && me == masterPlayer)
												{
													TakeOwnership(kvp.Key);
												}
												// the master player left, so everyone should set the owner null (we should get a new master shortly)
												else if (players[jm.userId] == masterPlayer)
												{
													kvp.Value.owner = null;
												}
											}
										}

										// TODO this may check for ownership in the future. We don't need ownership here
										deleteObjects.ForEach(NetworkDestroy);

										players.Remove(jm.userId);
									}
									else
									{
										// we got a join message, create it
										VelNetPlayer player = new VelNetPlayer
										{
											isLocal = false,
											room = jm.room,
											userid = jm.userId
										};
										players.Add(jm.userId, player);
										try
										{
											OnPlayerJoined?.Invoke(player);
										}
										// prevent errors in subscribers from breaking our code
										catch (Exception e)
										{
											Debug.LogError(e);
										}
									}
								}
								break; 
							
							}
						case DataMessage dm: {
								if (players.ContainsKey(dm.senderId))
								{
									players[dm.senderId]?.HandleMessage(dm); //todo
								}
								else
								{
									Debug.LogError("Received message from player that doesn't exist ");
								}

								break;

							
							}
						case ChangeMasterMessage cm: {

								if (masterPlayer == null)
								{
									masterPlayer = players[cm.masterId];

									// no master player yet, add the scene objects

									for (int i = 0; i < sceneObjects.Length; i++)
									{
										sceneObjects[i].networkId = -1 + "-" + i;
										sceneObjects[i].owner = masterPlayer;
										sceneObjects[i].isSceneObject = true; // needed for special handling when deleted
										objects.Add(sceneObjects[i].networkId, sceneObjects[i]);
									}
								}
								else
								{
									masterPlayer = players[cm.masterId];
								}

								masterPlayer.SetAsMasterPlayer();

								// master player should take over any objects that do not have an owner
								foreach (KeyValuePair<string, NetworkObject> kvp in objects)
								{
									kvp.Value.owner ??= masterPlayer;
								}

								break;
							
							}
					}
					
					//MessageReceived?.Invoke(m);
				}

				receivedMessages.Clear();
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


		/// <summary> 	
		/// Runs in background clientReceiveThread; Listens for incomming data. 	
		/// </summary>     
		/// 
		private byte[] ReadExact(NetworkStream stream, int N)
		{
			byte[] toReturn = new byte[N];

			int numRead = 0;
			int numLeft = N;
			while (numLeft > 0)
			{
				numRead += stream.Read(toReturn, numRead, numLeft);
				numLeft = N - numRead;
			}
			return toReturn;
		}

		private int GetIntFromBytes(byte[] bytes)
		{
			if (BitConverter.IsLittleEndian)
			{
				return BitConverter.ToInt32(bytes.Reverse().ToArray(),0);
			}
			else
			{
				return BitConverter.ToInt32(bytes, 0);
			}
		}
		private void ListenForData()
		{
			connected = true;
			try
			{
				socketConnection = new TcpClient(host, port);
				socketConnection.NoDelay = true;
				NetworkStream stream = socketConnection.GetStream();
				//now we are connected, so add a message to the queue
				AddMessage(new ConnectedMessage());
				//Join("MyRoom");
				//SendTo(MessageSendType.MESSAGE_OTHERS, Encoding.UTF8.GetBytes("Hello"));
				//FormGroup("close", new List<uint> { 1 });
				//SendToGroup("close", Encoding.UTF8.GetBytes("HelloGroup"));
				while (true)
				{
					
					// Get a stream object for reading 				
					

					//read a byte
					byte type = (byte)stream.ReadByte();
					
					if (type == 0) //login
					{
						LoginMessage m = new LoginMessage();
						m.userId = GetIntFromBytes(ReadExact(stream, 4)); //not really the sender...
						AddMessage(m);
					}
					else if(type == 1) //rooms
					{

						RoomsMessage m = new RoomsMessage();
						m.rooms = new List<ListedRoom>();
						int N = GetIntFromBytes(ReadExact(stream, 4)); //the size of the payload
						byte[] utf8data = ReadExact(stream, N);
						string roomMessage = Encoding.UTF8.GetString(utf8data);

						

						string[] sections = roomMessage.Split(',');
						foreach (string s in sections)
						{
							string[] pieces = s.Split(':');
							if (pieces.Length == 2) { 
								ListedRoom lr = new ListedRoom();
								lr.name = pieces[0];
								lr.numUsers = int.Parse(pieces[1]);
								m.rooms.Add(lr);
							}
						}
						AddMessage(m);
					}
					else if(type == 2) //joined
					{
						JoinMessage m = new JoinMessage();
						m.userId = GetIntFromBytes(ReadExact(stream, 4));
						int N = stream.ReadByte();
						byte[] utf8data = ReadExact(stream, N); //the room name, encoded as utf-8
						m.room = Encoding.UTF8.GetString(utf8data);
						AddMessage(m);
					}else if(type == 3) //data
					{
						DataMessage m = new DataMessage();
						m.senderId = GetIntFromBytes(ReadExact(stream, 4));
						int N = GetIntFromBytes(ReadExact(stream, 4)); //the size of the payload
						m.data = ReadExact(stream, N); //the message
						AddMessage(m);
					}
					else if(type == 4) //new master
					{
						ChangeMasterMessage m = new ChangeMasterMessage();
						m.masterId = (int)GetIntFromBytes(ReadExact(stream, 4)); //sender is the new master
						AddMessage(m);
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
					buffer[0] = 0;
					Array.Copy(get_be_bytes(userid), 0, buffer, 1, 4);
					udpSocket.SendTo(buffer, 5, SocketFlags.None, RemoteEndPoint);

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
					if (buffer[0] == 0)
					{
						Debug.Log("UDP connected");
					}else if (buffer[0] == 3)
					{
						DataMessage m = new DataMessage();
						//we should get the sender address
						byte[] senderBytes = new byte[4]; 
						Array.Copy(buffer, 1, senderBytes, 0, 4);
						m.senderId = GetIntFromBytes(senderBytes);
						byte[] messageBytes = new byte[numReceived - 5];
						Array.Copy(buffer, 5, messageBytes, 0, messageBytes.Length);
						m.data = messageBytes;
						AddMessage(m);
					}
				}
			}
			catch (Exception socketException)
			{
				Debug.Log("Socket exception: " + socketException);
			}
		}

		private static void SendUdpMessage(byte[] message, int N)
		{
			if (instance.udpSocket == null || !instance.udpConnected)
			{
				return;
			}

			instance.udpSocket.SendTo(message, N, SocketFlags.None, instance.RemoteEndPoint);
		}

		/// <summary> 	
		/// Send message to server using socket connection. 	
		/// </summary> 	
		private static void SendTcpMessage(byte[] message) //we can assume that this message is already formatted, so we just send it
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
			byte[] pB = Encoding.UTF8.GetBytes(password);
			writer.Write((byte)0);
			writer.Write((byte)uB.Length);
			writer.Write(uB);
			writer.Write((byte)pB.Length);
			writer.Write(pB);

			SendTcpMessage(stream.ToArray());
			

		}

		public static void GetRooms()
		{

			SendTcpMessage(new byte[1] { 1 }); //very simple message
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
			writer.Write((byte)R.Length);
			writer.Write(R);
			SendTcpMessage(stream.ToArray());


			
			
		}

		

		/// <summary>
		/// Leaves a room if we're in one
		/// </summary>
		public static void Leave()
		{
			if (InRoom)
			{
				Join(""); //super secret way to leave
			}
		}

		public static void SendToRoom(byte[] message, bool include_self = false, bool reliable = true, bool ordered = false)
		{
			byte sendType = (byte) MessageSendType.MESSAGE_OTHERS;
			if (include_self && ordered) sendType = (byte)MessageSendType.MESSAGE_ALL_ORDERED;
			if (include_self && !ordered) sendType = (byte)MessageSendType.MESSAGE_ALL;
			if (!include_self && ordered) sendType = (byte)MessageSendType.MESSAGE_OTHERS_ORDERED;

			
			if (reliable)
			{
				MemoryStream stream = new MemoryStream();
				BinaryWriter writer = new BinaryWriter(stream);
				writer.Write(sendType);
				writer.Write(get_be_bytes(message.Length));
				writer.Write(message);
				SendTcpMessage(stream.ToArray());
			}
			else
			{
				//udp message needs the type
				toSend[0] = sendType;  //we don't 
				Array.Copy(get_be_bytes(instance.userid), 0, toSend, 1, 4);
				Array.Copy(message, 0, toSend, 5, message.Length);
				SendUdpMessage(toSend,message.Length+5); //shouldn't be over 1024...
			}
		}


		public static void SendToGroup(string group, byte[] message, bool reliable = true)
		{
			byte[] utf8bytes = Encoding.UTF8.GetBytes(group);
			if (reliable)
			{
				MemoryStream stream = new MemoryStream();
				BinaryWriter writer = new BinaryWriter(stream);
				writer.Write((byte)MessageSendType.MESSAGE_GROUP);
				writer.Write(get_be_bytes(message.Length));
				writer.Write(message);
				writer.Write((byte)utf8bytes.Length);
				writer.Write(utf8bytes);
				SendTcpMessage(stream.ToArray());
			}
			else
			{
				toSend[0] = (byte)MessageSendType.MESSAGE_GROUP;
				Array.Copy(get_be_bytes(instance.userid), 0, toSend, 1, 4);
				//also need to send the group
				toSend[5] = (byte)utf8bytes.Length;
				Array.Copy(utf8bytes, 0, toSend, 6, utf8bytes.Length);
				Array.Copy(message, 0, toSend, 6+utf8bytes.Length, message.Length);
				SendUdpMessage(toSend, 6 + utf8bytes.Length + message.Length);
			}
		}

		/// <summary>
		/// changes the designated group that sendto(4) will go to
		/// </summary>
		public static void SetupMessageGroup(string groupname, List<int> client_ids)
		{
			if (client_ids.Count > 0)
			{
				instance.groups[groupname] = client_ids.ToList();
			}

			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);
			byte[] R = Encoding.UTF8.GetBytes(groupname);
			writer.Write((byte)6);
			writer.Write((byte)R.Length);
			writer.Write(R);
			writer.Write(get_be_bytes(client_ids.Count * 4));
			for (int i = 0; i < client_ids.Count; i++)
			{
				writer.Write(get_be_bytes(client_ids[i]));
			}
			SendTcpMessage(stream.ToArray());
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
			SendToRoom(Encoding.UTF8.GetBytes("7," + newObject.networkId + "," + prefabName),false,true);

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
			SendToRoom(Encoding.UTF8.GetBytes("6," + networkId));

			return true;
		}
	}
}