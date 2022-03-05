using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Net;
using UnityEngine.SceneManagement;
using System.IO;

namespace VelNet
{
	[AddComponentMenu("VelNet/VelNet Manager")]
	public class VelNetManager : MonoBehaviour
	{
		public enum MessageReceivedType
		{
			LOGGED_IN = 0,
			ROOM_LIST = 1,
			PLAYER_JOINED = 2,
			DATA_MESSAGE = 3,
			MASTER_MESSAGE = 4,
			YOU_JOINED = 5,
			PLAYER_LEFT = 6,
			YOU_LEFT = 7,
			ROOM_DATA = 8
		}

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
			MESSAGE_SETGROUP = 6,
			MESSAGE_GETROOMDATA = 9
		};

		public enum MessageType : byte
		{
			ObjectSync,
			TakeOwnership,
			Instantiate,
			Destroy,
			DeleteSceneObjects,
			Custom
		}

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

		public readonly Dictionary<int, VelNetPlayer> players = new Dictionary<int, VelNetPlayer>();

		#region Callbacks

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
		public static Action OnLoggedIn;
		public static Action<RoomsMessage> RoomsReceived;
		public static Action<RoomDataMessage> RoomDataReceived;

		public static Action<Message> MessageReceived;
		public static Action<int, byte[]> CustomMessageReceived;

		#endregion

		public bool connected;
		private bool wasConnected;
		private double lastConnectionCheck;

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
		public static VelNetPlayer LocalPlayer => instance != null ? instance.players.Where(p => p.Value.isLocal).Select(p => p.Value).FirstOrDefault() : null;
		public static bool InRoom => LocalPlayer != null && LocalPlayer.room != "-1" && LocalPlayer.room != "";
		public static string Room => LocalPlayer?.room;

		public static List<VelNetPlayer> Players => instance.players.Values.ToList();

		/// <summary>
		/// The player count in this room.
		/// -1 if not in a room.
		/// </summary>
		public static int PlayerCount => instance.players.Count;

		public static bool IsConnected => instance != null && instance.connected && instance.udpConnected;

		// this is for sending udp packets
		private static readonly byte[] toSend = new byte[1024];

		// Use this for initialization
		public abstract class Message
		{
		}

		public class ListedRoom
		{
			public string name;
			public int numUsers;

			public override string ToString()
			{
				return "Room Name: " + name + "\tUsers: " + numUsers;
			}
		}

		public class LoginMessage : Message
		{
			public int userId;
		}

		public class RoomsMessage : Message
		{
			public List<ListedRoom> rooms;

			public override string ToString()
			{
				return string.Join("\n", rooms);
			}
		}

		public class RoomDataMessage : Message
		{
			public string room;
			public readonly List<Tuple<int, string>> members = new List<Tuple<int, string>>();
		}

		public class JoinMessage : Message
		{
			public int userId;
			public string room;
		}

		public class DataMessage : Message
		{
			public int senderId;
			public byte[] data;
		}

		public class ChangeMasterMessage : Message
		{
			public int masterId;
		}

		public class YouJoinedMessage : Message
		{
			public List<int> ids;
			public string room;
		}

		public class PlayerLeftMessage : Message
		{
			public int userId;
			public string room;
		}

		public class YouLeftMessage : Message
		{
			public string room;
		}

		public class ConnectedMessage : Message
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
			
			ConnectToServer();
		}

		private void AddMessage(Message m)
		{
			lock (receivedMessages)
			{
				//Debug.Log(messagesReceived++);
				receivedMessages.Add(m);
			}

			try
			{
				MessageReceived?.Invoke(m);
			}
			catch (Exception e)
			{
				Debug.LogError(e);
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
						case ConnectedMessage msg:
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
							if (userid == lm.userId)
							{
								Debug.Log("Received duplicate login message " + userid);
								return;
							}
							userid = lm.userId;
							Debug.Log("Joined server " + userid);

							try
							{
								OnLoggedIn?.Invoke();
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								Debug.LogError(e);
							}

							//start the udp thread 
							clientReceiveThreadUDP?.Abort();
							clientReceiveThreadUDP = new Thread(ListenForDataUDP);
							clientReceiveThreadUDP.Start();

							break;
						}
						case RoomsMessage rm:
						{
							try
							{
								RoomsReceived?.Invoke(rm);
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								Debug.LogError(e);
							}

							break;
						}
						case RoomDataMessage rdm:
						{
							try
							{
								RoomDataReceived?.Invoke(rdm);
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								Debug.LogError(e);
							}

							break;
						}
						case YouJoinedMessage jm:
						{
							// we clear the list, but will recreate as we get messages from people in our room
							players.Clear();
							masterPlayer = null;


							foreach (int playerId in jm.ids)
							{
								VelNetPlayer player = new VelNetPlayer
								{
									room = jm.room,
									userid = playerId,
									isLocal = playerId == userid,
								};
								players.Add(player.userid, player);
							}

							try
							{
								Debug.Log(jm.room);
								OnJoinedRoom?.Invoke(jm.room);
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								Debug.LogError(e);
							}

							foreach (KeyValuePair<int, VelNetPlayer> kvp in players)
							{
								if (kvp.Key != userid)
								{
									try
									{
										OnPlayerJoined?.Invoke(kvp.Value);
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
						case YouLeftMessage msg:
						{
							LeaveRoom();
							break;
						}
						case PlayerLeftMessage lm:
						{
							VelNetPlayer me = players[userid];
							// we got a left message, kill it
							// change ownership of all objects to master
							List<string> deleteObjects = new List<string>();
							foreach (KeyValuePair<string, NetworkObject> kvp in objects)
							{
								if (kvp.Value.owner == players[lm.userId]) // the owner is the player that left
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
									else if (players[lm.userId] == masterPlayer)
									{
										kvp.Value.owner = null;
									}
								}
							}

							// TODO this may check for ownership in the future. We don't need ownership here
							deleteObjects.ForEach(NetworkDestroy);

							VelNetPlayer leftPlayer = players[lm.userId];
							players.Remove(lm.userId);

							try
							{
								OnPlayerLeft?.Invoke(leftPlayer);
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								Debug.LogError(e);
							}

							break;
						}
						case JoinMessage jm:
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


							break;
						}
						case DataMessage dm:
						{
							if (players.ContainsKey(dm.senderId))
							{
								players[dm.senderId]?.HandleMessage(dm);
							}
							else
							{
								Debug.LogError("Received message from player that doesn't exist ");
							}

							break;
						}
						case ChangeMasterMessage cm:
						{
							if (masterPlayer == null)
							{
								masterPlayer = players[cm.masterId];

								// no master player yet, add the scene objects

								for (int i = 0; i < sceneObjects.Length; i++)
								{
									if (sceneObjects[i].sceneNetworkId == 0)
									{
										Debug.LogError("Scene Network ID is 0. Make sure to assign one first.", sceneObjects[i]);
									}

									sceneObjects[i].networkId = -1 + "-" + sceneObjects[i].sceneNetworkId;
									sceneObjects[i].owner = masterPlayer;
									sceneObjects[i].isSceneObject = true; // needed for special handling when deleted

									if (objects.ContainsKey(sceneObjects[i].networkId))
									{
										Debug.LogError($"Duplicate NetworkID: {sceneObjects[i].networkId} {sceneObjects[i].name} {objects[sceneObjects[i].networkId]}");
									}
									else
									{
										objects.Add(sceneObjects[i].networkId, sceneObjects[i]);
									}
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


			if (Time.timeAsDouble - lastConnectionCheck > 2)
			{
				if (!IsConnected && wasConnected)
				{
					Debug.Log("Reconnecting...");
					ConnectToServer();
				}

				lastConnectionCheck = Time.timeAsDouble;
			}
		}

		private void LeaveRoom()
		{
			string oldRoom = LocalPlayer?.room;
			// delete all NetworkObjects that aren't scene objects or are null now
			objects
				.Where(kvp => kvp.Value == null || !kvp.Value.isSceneObject)
				.Select(o => o.Key)
				.ToList().ForEach(NetworkDestroy);

			// then remove references to the ones that are left
			objects.Clear();

			// empty all the groups
			foreach (string group in instance.groups.Keys)
			{
				SetupMessageGroup(@group, new List<int>());
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
			
			foreach (NetworkObject s in sceneObjects)
			{
				s.owner = null;
			}
			
			players.Clear();
			masterPlayer = null;
		}

		private void OnApplicationQuit()
		{
			socketConnection?.Close();
			clientReceiveThreadUDP?.Abort();
			clientReceiveThread?.Abort();
		}

		/// <summary> 	
		/// Setup socket connection. 	
		/// </summary> 	
		public static void ConnectToServer()
		{
			try
			{
				instance.clientReceiveThread = new Thread(instance.ListenForData);
				instance.clientReceiveThread.Start();
			}
			catch (Exception e)
			{
				Debug.Log("On client connect exception " + e);
			}
		}

		private void DisconnectFromServer()
		{
			LeaveRoom();
			connected = false;
			udpConnected = false;
			socketConnection?.Close();
			clientReceiveThreadUDP?.Abort();
			clientReceiveThread?.Abort();
			socketConnection = null;
			udpSocket = null;
		}


		/// <summary> 	
		/// Runs in background clientReceiveThread; Listens for incoming data. 	
		/// </summary>
		private static byte[] ReadExact(Stream stream, int N)
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

		private static int GetIntFromBytes(byte[] bytes)
		{
			return BitConverter.ToInt32(BitConverter.IsLittleEndian ? bytes.Reverse().ToArray() : bytes, 0);
		}

		private void ListenForData()
		{
			connected = true;
			try
			{
				socketConnection = new TcpClient(host, port);
				socketConnection.NoDelay = true;
				// Get a stream object for reading
				NetworkStream stream = socketConnection.GetStream();
				using BinaryReader reader = new BinaryReader(stream);
				//now we are connected, so add a message to the queue
				AddMessage(new ConnectedMessage());
				while (true)
				{
					//read a byte
					MessageReceivedType type = (MessageReceivedType)stream.ReadByte();

					switch (type)
					{
						//login
						case MessageReceivedType.LOGGED_IN:
						{
							LoginMessage m = new LoginMessage();
							m.userId = GetIntFromBytes(ReadExact(stream, 4)); //not really the sender...
							AddMessage(m);
							break;
						}
						//rooms
						case MessageReceivedType.ROOM_LIST:
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
								if (pieces.Length == 2)
								{
									ListedRoom lr = new ListedRoom();
									lr.name = pieces[0];
									lr.numUsers = int.Parse(pieces[1]);
									m.rooms.Add(lr);
								}
							}

							AddMessage(m);
							break;
						}
						case MessageReceivedType.ROOM_DATA:
						{
							RoomDataMessage rdm = new RoomDataMessage();
							int N = stream.ReadByte();
							byte[] utf8data = ReadExact(stream, N); //the room name, encoded as utf-8
							string roomname = Encoding.UTF8.GetString(utf8data);

							N = GetIntFromBytes(ReadExact(stream, 4)); //the number of client datas to read
							rdm.room = roomname;
							for (int i = 0; i < N; i++)
							{
								// client id + short string
								int client_id = GetIntFromBytes(ReadExact(stream, 4));
								int s = stream.ReadByte(); //size of string
								utf8data = ReadExact(stream, s); //the username
								string username = Encoding.UTF8.GetString(utf8data);
								rdm.members.Add(new Tuple<int, string>(client_id, username));
								Debug.Log(username);
							}

							AddMessage(rdm);
							break;
						}
						//joined
						case MessageReceivedType.PLAYER_JOINED:
						{
							JoinMessage m = new JoinMessage();
							m.userId = GetIntFromBytes(ReadExact(stream, 4));
							int N = stream.ReadByte();
							byte[] utf8data = ReadExact(stream, N); //the room name, encoded as utf-8
							m.room = Encoding.UTF8.GetString(utf8data);
							AddMessage(m);
							break;
						}
						//data
						case MessageReceivedType.DATA_MESSAGE:
						{
							DataMessage m = new DataMessage();
							m.senderId = GetIntFromBytes(ReadExact(stream, 4));
							int N = GetIntFromBytes(ReadExact(stream, 4)); //the size of the payload
							m.data = ReadExact(stream, N); //the message
							AddMessage(m);
							break;
						}
						//new master
						case MessageReceivedType.MASTER_MESSAGE:
						{
							ChangeMasterMessage m = new ChangeMasterMessage();
							m.masterId = GetIntFromBytes(ReadExact(stream, 4)); //sender is the new master
							AddMessage(m);
							break;
						}

						case MessageReceivedType.YOU_JOINED:
						{
							YouJoinedMessage m = new YouJoinedMessage();
							int N = GetIntFromBytes(ReadExact(stream, 4));
							m.ids = new List<int>();
							for (int i = 0; i < N; i++)
							{
								m.ids.Add(GetIntFromBytes(ReadExact(stream, 4)));
							}

							N = stream.ReadByte();
							byte[] utf8data = ReadExact(stream, N); //the room name, encoded as utf-8
							m.room = Encoding.UTF8.GetString(utf8data);
							AddMessage(m);
							break;
						}
						case MessageReceivedType.PLAYER_LEFT:
						{
							PlayerLeftMessage m = new PlayerLeftMessage();
							m.userId = GetIntFromBytes(ReadExact(stream, 4));
							int N = stream.ReadByte();
							byte[] utf8data = ReadExact(stream, N); //the room name, encoded as utf-8
							m.room = Encoding.UTF8.GetString(utf8data);
							AddMessage(m);
							break;
						}
						case MessageReceivedType.YOU_LEFT:
						{
							YouLeftMessage m = new YouLeftMessage();
							int N = stream.ReadByte();
							byte[] utf8data = ReadExact(stream, N); //the room name, encoded as utf-8
							m.room = Encoding.UTF8.GetString(utf8data);
							AddMessage(m);
							break;
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
				wasConnected = true;
				while (true)
				{
					int numReceived = udpSocket.Receive(buffer);
					switch ((MessageReceivedType)buffer[0])
					{
						case MessageReceivedType.LOGGED_IN:
							Debug.Log("UDP connected");
							break;
						case MessageReceivedType.DATA_MESSAGE:
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
							break;
						}
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
		/// <param name="message">We can assume that this message is already formatted, so we just send it</param>
		/// </summary> 	
		private static void SendTcpMessage(byte[] message) 
		{
			// Debug.Log("Sent: " + clientMessage);
			if (instance.socketConnection == null)
			{
				Debug.LogError("Tried to send message while socket connection was still null.", instance);
				return;
			}

			try
			{
				// check if we have been disconnected, if so shut down velnet
				if (!instance.socketConnection.Connected)
				{
					instance.DisconnectFromServer();
					Debug.LogError("Disconnected from server. Most likely due to timeout.");
					return;
				}
				
				// Get a stream object for writing. 			
				NetworkStream stream = instance.socketConnection.GetStream();
				if (stream.CanWrite)
				{
					stream.Write(message, 0, message.Length);
				}
			}
			catch (SocketException socketException)
			{
				Debug.Log("Socket exception: " + socketException);
			}
		}

		private static byte[] get_be_bytes(int n)
		{
			return BitConverter.GetBytes(n).Reverse().ToArray();
		}

		/// <summary>
		/// Connects to the server for a particular app
		/// </summary>
		/// <param name="appName">A unique name for your app. Communication can only happen between clients with the same app name.</param>
		/// <param name="deviceId">Should be unique per device that connects. e.g. md5(deviceUniqueIdentifier)</param>
		public static void Login(string appName, string deviceId)
		{
			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);

			byte[] id = Encoding.UTF8.GetBytes(deviceId);
			byte[] app = Encoding.UTF8.GetBytes(appName);
			writer.Write((byte)MessageSendType.MESSAGE_LOGIN);
			writer.Write((byte)id.Length);
			writer.Write(id);
			writer.Write((byte)app.Length);
			writer.Write(app);

			SendTcpMessage(stream.ToArray());
		}

		public static void GetRooms(Action<RoomsMessage> callback = null)
		{
			SendTcpMessage(new byte[] { (byte)MessageSendType.MESSAGE_GETROOMS }); // very simple message

			if (callback != null)
			{
				RoomsReceived += RoomsReceivedCallback;
			}

			void RoomsReceivedCallback(RoomsMessage msg)
			{
				callback(msg);
				RoomsReceived -= RoomsReceivedCallback;
			}
		}

		public static void GetRoomData(string roomName)
		{
			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);

			byte[] R = Encoding.UTF8.GetBytes(roomName);
			writer.Write((byte)MessageSendType.MESSAGE_GETROOMDATA);
			writer.Write((byte)R.Length);
			writer.Write(R);
			SendTcpMessage(stream.ToArray());
		}

		/// <summary>
		/// Joins a room by name
		/// </summary>
		/// <param name="roomName">The name of the room to join</param>
		public static void Join(string roomName)
		{
			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);

			byte[] R = Encoding.UTF8.GetBytes(roomName);
			writer.Write((byte)MessageSendType.MESSAGE_JOINROOM);
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
				Join(""); // super secret way to leave
			}
		}

		public static void SendCustomMessage(byte[] message, bool include_self = false, bool reliable = true, bool ordered = false)
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.Custom);
			writer.Write(message.Length);
			writer.Write(message);
			SendToRoom(mem.ToArray(), include_self, reliable, ordered);
		}

		public static void SendCustomMessageToGroup(string group, byte[] message, bool reliable = true)
		{
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.Custom);
			writer.Write(message.Length);
			writer.Write(message);
			SendToGroup(group, mem.ToArray(), reliable);
		}

		internal static void SendToRoom(byte[] message, bool include_self = false, bool reliable = true, bool ordered = false)
		{
			byte sendType = (byte)MessageSendType.MESSAGE_OTHERS;
			if (include_self && ordered) sendType = (byte)MessageSendType.MESSAGE_ALL_ORDERED;
			if (include_self && !ordered) sendType = (byte)MessageSendType.MESSAGE_ALL;
			if (!include_self && ordered) sendType = (byte)MessageSendType.MESSAGE_OTHERS_ORDERED;


			if (reliable)
			{
				MemoryStream mem = new MemoryStream();
				BinaryWriter writer = new BinaryWriter(mem);
				writer.Write(sendType);
				writer.Write(get_be_bytes(message.Length));
				writer.Write(message);
				SendTcpMessage(mem.ToArray());
			}
			else
			{
				//udp message needs the type
				toSend[0] = sendType; //we don't 
				Array.Copy(get_be_bytes(instance.userid), 0, toSend, 1, 4);
				Array.Copy(message, 0, toSend, 5, message.Length);
				SendUdpMessage(toSend, message.Length + 5); //shouldn't be over 1024...
			}
		}


		internal static void SendToGroup(string group, byte[] message, bool reliable = true)
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
				Array.Copy(message, 0, toSend, 6 + utf8bytes.Length, message.Length);
				SendUdpMessage(toSend, 6 + utf8bytes.Length + message.Length);
			}
		}

		/// <summary>
		/// changes the designated group that SendTo(4) will go to
		/// </summary>
		public static void SetupMessageGroup(string groupName, List<int> clientIds)
		{
			if (clientIds.Count > 0)
			{
				instance.groups[groupName] = clientIds.ToList();
			}

			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);
			byte[] R = Encoding.UTF8.GetBytes(groupName);
			writer.Write((byte)6);
			writer.Write((byte)R.Length);
			writer.Write(R);
			writer.Write(get_be_bytes(clientIds.Count * 4));
			foreach (int c in clientIds)
			{
				writer.Write(get_be_bytes(c));
			}

			SendTcpMessage(stream.ToArray());
		}

		[Obsolete("Use NetworkInstantiate instead. This matches the naming convention of NetworkDestroy")]
		public static NetworkObject InstantiateNetworkObject(string prefabName)
		{
			return NetworkInstantiate(prefabName);
		}

		public static NetworkObject NetworkInstantiate(string prefabName)
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
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.Instantiate);
			writer.Write(newObject.networkId);
			writer.Write(prefabName);
			SendToRoom(mem.ToArray(), include_self: false, reliable: true);

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
			
			// clean up if this is null
			if (obj == null)
			{
				instance.objects.Remove(networkId);
				Debug.LogError("Object to delete was already null");
				return;
			}
			
			// Delete locally immediately
			SomebodyDestroyedNetworkObject(networkId);

			// Only sent to others, as we already deleted this. 
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.Destroy);
			writer.Write(networkId);
			SendToRoom(mem.ToArray(), include_self: false, reliable: true);

		}
		
		
		public static void SomebodyDestroyedNetworkObject(string networkId)
		{
			if (!instance.objects.ContainsKey(networkId)) return;
			NetworkObject obj = instance.objects[networkId];
			if (obj == null)
			{
				instance.objects.Remove(networkId);
				Debug.LogError("Object to delete was already null");
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
			if (LocalPlayer == null)
			{
				Debug.LogError("Can't take ownership. No local player.");
				return false;
			}

			// obj must exist
			if (!instance.objects.ContainsKey(networkId))
			{
				Debug.LogError("Can't take ownership. Object with that network id doesn't exist: " + networkId);
				return false;
			}

			// if the ownership is locked, fail
			if (instance.objects[networkId].ownershipLocked)
			{
				Debug.LogError("Can't take ownership. Ownership for this object is locked.");
				return false;
			}

			// immediately successful
			instance.objects[networkId].owner = LocalPlayer;

			// must be ordered, so that ownership transfers are not confused.
			// Also sent to all players, so that multiple simultaneous requests will result in the same outcome.
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.TakeOwnership);
			writer.Write(networkId);
			SendToRoom(mem.ToArray(), false, true);

			return true;
		}
	}
}