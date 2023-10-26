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
using System.Threading.Tasks;

namespace VelNet
{
	/// <summary>Used to flag methods as remote-callable.</summary>
	public class VelNetRPC : Attribute
	{
	}

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
			InstantiateWithTransform,
			InstantiateWithState,
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

		[Tooltip("Sends debug messages about connection and join events")]
		public bool debugMessages;

		[Tooltip("Automatically logs in with app name and hash of device id after connecting")]
		public bool autoLogin = true;

		[Tooltip("Uses the version number in the login to prevent crosstalk between different app versions")]
		public bool onlyConnectToSameVersion;

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
		/// bool is true when this is a join message for someone that was already in the room when we joined it
		/// </summary>
		public static Action<VelNetPlayer, bool> OnPlayerJoined;

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
		private bool offlineMode;
		private string offlineRoomName;

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

		public static VelNetPlayer LocalPlayer => instance != null
			? instance.players.Where(p => p.Value.isLocal).Select(p => p.Value).FirstOrDefault()
			: null;

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
			public readonly List<(int, string)> members = new List<(int, string)>();

			public override string ToString()
			{
				return room + "\n" + string.Join("\n", members.Select(m => $"{m.Item1}\t{m.Item2}"));
			}
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
			public List<int> playerIds;
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

		private const int maxUnreadMessages = 1000;
		private readonly List<Message> receivedMessages = new List<Message>();

		private void Awake()
		{
			SceneManager.sceneLoaded += (_, _) =>
			{
				// add all local network objects
				sceneObjects = FindObjectsOfType<NetworkObject>().Where(o => o.isSceneObject).ToArray();
			};
		}

		private void OnEnable()
		{
			if (instance != null)
			{
				VelNetLogger.Error("Multiple NetworkManagers detected! Bad!", this);
			}

			instance = this;
			ConnectToServer();
		}

		private void OnDisable()
		{
			DisconnectFromServer();
			instance = null;
		}

		private void AddMessage(Message m)
		{
			bool added = false;
			lock (receivedMessages)
			{
				// this is to avoid backups when headset goes to sleep
				if (receivedMessages.Count < maxUnreadMessages)
				{
					receivedMessages.Add(m);
					added = true;
				}
			}

			try
			{
				if (added) MessageReceived?.Invoke(m);
			}
			catch (Exception e)
			{
				VelNetLogger.Error(e.ToString());
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
							VelNetLogger.Info("Connected to server.");
							try
							{
								OnConnectedToServer?.Invoke();
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								VelNetLogger.Error(e.ToString());
							}

							if (autoLogin)
							{
								Login(
									onlyConnectToSameVersion
										? $"{Application.productName}_{Application.version}"
										: $"{Application.productName}",
									Hash128.Compute(SystemInfo.deviceUniqueIdentifier + Application.productName)
										.ToString()
								);
							}

							break;
						}
						case LoginMessage lm:
						{
							if (userid == lm.userId)
							{
								VelNetLogger.Error("Received duplicate login message " + userid);
							}

							userid = lm.userId;
							VelNetLogger.Info("Logged in: " + userid);

							try
							{
								OnLoggedIn?.Invoke();
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								VelNetLogger.Error(e.ToString(), this);
							}

							//start the udp thread 
							clientReceiveThreadUDP?.Abort();
							clientReceiveThreadUDP = new Thread(ListenForDataUDP);
							clientReceiveThreadUDP.Start();

							break;
						}
						case RoomsMessage rm:
						{
							VelNetLogger.Info($"Received rooms:\n{rm}");
							try
							{
								RoomsReceived?.Invoke(rm);
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								VelNetLogger.Error(e.ToString(), this);
							}

							break;
						}
						case RoomDataMessage rdm:
						{
							VelNetLogger.Info($"Received room data:\n{rdm}");
							try
							{
								RoomDataReceived?.Invoke(rdm);
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								VelNetLogger.Error(e.ToString(), this);
							}

							break;
						}
						case YouJoinedMessage jm:
						{
							VelNetLogger.Info($"Joined Room: {jm.room} \t ({jm.playerIds.Count} players)");

							// we clear the list, but will recreate as we get messages from people in our room
							players.Clear();
							masterPlayer = null;


							foreach (int playerId in jm.playerIds)
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
								OnJoinedRoom?.Invoke(jm.room);
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								VelNetLogger.Error(e.ToString(), this);
							}

							foreach (KeyValuePair<int, VelNetPlayer> kvp in players)
							{
								if (kvp.Key != userid)
								{
									try
									{
										OnPlayerJoined?.Invoke(kvp.Value, true);
									}
									// prevent errors in subscribers from breaking our code
									catch (Exception e)
									{
										VelNetLogger.Error(e.ToString(), this);
									}
								}
							}


							break;
						}
						case YouLeftMessage msg:
						{
							LeaveRoomInternal();
							break;
						}
						case PlayerLeftMessage lm:
						{
							VelNetLogger.Info($"Player left: {lm.userId}");

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
								VelNetLogger.Error(e.ToString(), this);
							}

							break;
						}
						case JoinMessage jm:
						{
							VelNetLogger.Info($"Player joined: {jm.userId}");

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
								OnPlayerJoined?.Invoke(player, false);
							}
							// prevent errors in subscribers from breaking our code
							catch (Exception e)
							{
								VelNetLogger.Error(e.ToString(), this);
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
								LocalPlayer?.HandleMessage(dm, true);
							}

							break;
						}
						case ChangeMasterMessage cm:
						{
							VelNetLogger.Info($"Master client changed: {cm.masterId}");

							if (masterPlayer == null)
							{
								if (players.ContainsKey(cm.masterId))
								{
									masterPlayer = players[cm.masterId];
								}
								else
								{
									masterPlayer = players.Aggregate((p1, p2) =>
										p1.Value.userid.CompareTo(p2.Value.userid) > 0 ? p1 : p2).Value;
									VelNetLogger.Error(
										"Got an invalid master client id from the server. Using fallback.");
								}

								// no master player yet, add the scene objects

								for (int i = 0; i < sceneObjects.Length; i++)
								{
									if (sceneObjects[i].sceneNetworkId == 0)
									{
										VelNetLogger.Error("Scene Network ID is 0. Make sure to assign one first.",
											sceneObjects[i]);
									}

									sceneObjects[i].networkId = -1 + "-" + sceneObjects[i].sceneNetworkId;
									sceneObjects[i].owner = masterPlayer;
									sceneObjects[i].isSceneObject = true; // needed for special handling when deleted
									try
									{
										sceneObjects[i].OwnershipChanged?.Invoke(masterPlayer);
									}
									catch (Exception e)
									{
										VelNetLogger.Error("Error in event handling.\n" + e);
									}

									if (objects.ContainsKey(sceneObjects[i].networkId))
									{
										VelNetLogger.Error(
											$"Duplicate NetworkID: {sceneObjects[i].networkId} {sceneObjects[i].name} {objects[sceneObjects[i].networkId]}");
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

			// reconnection
			if (Time.timeAsDouble - lastConnectionCheck > 2)
			{
				if (!IsConnected && wasConnected)
				{
					VelNetLogger.Info("Reconnecting...");
					ConnectToServer();
				}

				lastConnectionCheck = Time.timeAsDouble;
			}
		}

		private void LeaveRoomInternal()
		{
			VelNetLogger.Info("Leaving Room");
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
				VelNetLogger.Error(e.ToString(), this);
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
		private static void ConnectToServer()
		{
			try
			{
				instance.clientReceiveThread = new Thread(instance.ListenForData);
				instance.clientReceiveThread.Start();
			}
			catch (Exception e)
			{
				VelNetLogger.Error("On client connect exception " + e);
			}
		}

		private void DisconnectFromServer()
		{
			VelNetLogger.Info("Disconnecting from server...");
			string oldRoom = LocalPlayer?.room;
			// delete all NetworkObjects that aren't scene objects or are null now
			objects
				.Where(kvp => kvp.Value == null || !kvp.Value.isSceneObject)
				.Select(o => o.Key)
				.ToList().ForEach(SomebodyDestroyedNetworkObject);

			// then remove references to the ones that are left
			objects.Clear();

			instance.groups.Clear();

			foreach (NetworkObject s in sceneObjects)
			{
				s.owner = null;
			}

			players.Clear();
			masterPlayer = null;


			connected = false;
			udpConnected = false;
			socketConnection?.Close();
			clientReceiveThreadUDP?.Abort();
			clientReceiveThread?.Abort();
			clientReceiveThread = null;
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
				while (socketConnection.Connected)
				{
					HandleIncomingMessage(reader);
				}
			}
			catch (ThreadAbortException)
			{
				// pass
			}
			catch (SocketException socketException)
			{
				VelNetLogger.Error("Socket exception: " + socketException);
				VelNetLogger.Error("Switching to offline mode");
				offlineMode = true;
				AddMessage(new ConnectedMessage());
			}
			catch (Exception ex)
			{
				VelNetLogger.Error(ex.ToString());
			}

			connected = false;
		}

		private void HandleIncomingMessage(BinaryReader reader)
		{
			MessageReceivedType type = (MessageReceivedType)reader.ReadByte();
			switch (type)
			{
				//login
				case MessageReceivedType.LOGGED_IN:
				{
					AddMessage(new LoginMessage
					{
						// not really the sender...
						userId = GetIntFromBytes(reader.ReadBytes(4))
					});
					break;
				}
				//rooms
				case MessageReceivedType.ROOM_LIST:
				{
					RoomsMessage m = new RoomsMessage();
					m.rooms = new List<ListedRoom>();
					int N = GetIntFromBytes(reader.ReadBytes(4)); //the size of the payload
					byte[] utf8data = reader.ReadBytes(N);
					string roomMessage = Encoding.UTF8.GetString(utf8data);

					string[] sections = roomMessage.Split(',');
					foreach (string s in sections)
					{
						string[] pieces = s.Split(':');
						if (pieces.Length == 2)
						{
							m.rooms.Add(new ListedRoom
							{
								name = pieces[0],
								numUsers = int.Parse(pieces[1])
							});
						}
					}

					AddMessage(m);
					break;
				}
				case MessageReceivedType.ROOM_DATA:
				{
					RoomDataMessage rdm = new RoomDataMessage();
					int N = reader.ReadByte();
					byte[] utf8data = reader.ReadBytes(N); //the room name, encoded as utf-8
					string roomname = Encoding.UTF8.GetString(utf8data);

					N = GetIntFromBytes(reader.ReadBytes(4)); //the number of client datas to read
					rdm.room = roomname;
					for (int i = 0; i < N; i++)
					{
						// client id + short string
						int clientId = GetIntFromBytes(reader.ReadBytes(4));
						int s = reader.ReadByte(); //size of string
						utf8data = reader.ReadBytes(s); //the username
						string username = Encoding.UTF8.GetString(utf8data);
						rdm.members.Add((clientId, username));
					}

					AddMessage(rdm);
					break;
				}
				//joined
				case MessageReceivedType.PLAYER_JOINED:
				{
					JoinMessage m = new JoinMessage();
					m.userId = GetIntFromBytes(reader.ReadBytes(4));
					int N = reader.ReadByte();
					byte[] utf8data = reader.ReadBytes(N); //the room name, encoded as utf-8
					m.room = Encoding.UTF8.GetString(utf8data);
					AddMessage(m);
					break;
				}
				//data
				case MessageReceivedType.DATA_MESSAGE:
				{
					DataMessage m = new DataMessage();
					m.senderId = GetIntFromBytes(reader.ReadBytes(4));
					int N = GetIntFromBytes(reader.ReadBytes(4)); //the size of the payload
					m.data = reader.ReadBytes(N); //the message
					AddMessage(m);
					break;
				}
				// new master
				case MessageReceivedType.MASTER_MESSAGE:
				{
					ChangeMasterMessage m = new ChangeMasterMessage();
					m.masterId = GetIntFromBytes(reader.ReadBytes(4)); // sender is the new master
					AddMessage(m);
					break;
				}

				case MessageReceivedType.YOU_JOINED:
				{
					YouJoinedMessage m = new YouJoinedMessage();
					int N = GetIntFromBytes(reader.ReadBytes(4));
					m.playerIds = new List<int>();
					for (int i = 0; i < N; i++)
					{
						m.playerIds.Add(GetIntFromBytes(reader.ReadBytes(4)));
					}

					N = reader.ReadByte();
					byte[] utf8data = reader.ReadBytes(N); //the room name, encoded as utf-8
					m.room = Encoding.UTF8.GetString(utf8data);
					AddMessage(m);
					break;
				}
				case MessageReceivedType.PLAYER_LEFT:
				{
					PlayerLeftMessage m = new PlayerLeftMessage();
					m.userId = GetIntFromBytes(reader.ReadBytes(4));
					int N = reader.ReadByte();
					byte[] utf8data = reader.ReadBytes(N); //the room name, encoded as utf-8
					m.room = Encoding.UTF8.GetString(utf8data);
					AddMessage(m);
					break;
				}
				case MessageReceivedType.YOU_LEFT:
				{
					YouLeftMessage m = new YouLeftMessage();
					int N = reader.ReadByte();
					byte[] utf8data = reader.ReadBytes(N); //the room name, encoded as utf-8
					m.room = Encoding.UTF8.GetString(utf8data);
					AddMessage(m);
					break;
				}
				default:
					VelNetLogger.Error("Unknown message type");
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}
		}

		private void ListenForDataUDP()
		{
			if (offlineMode) return;

			//I don't yet have a UDP connection
			try
			{
				IPAddress[] addresses = Dns.GetHostAddresses(host);
				Debug.Assert(addresses.Length > 0);
				RemoteEndPoint = new IPEndPoint(addresses[0], port);

				udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

				udpConnected = false;
				byte[] buffer = new byte[1024];
				while (true)
				{
					buffer[0] = 0;
					Array.Copy(GetBigEndianBytes(userid), 0, buffer, 1, 4);
					udpSocket.SendTo(buffer, 5, SocketFlags.None, RemoteEndPoint);

					if (udpSocket.Available == 0)
					{
						Thread.Sleep(100);
						VelNetLogger.Info("Waiting for UDP response");
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
							VelNetLogger.Info("UDP connected");
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
			catch (ThreadAbortException)
			{
				// pass
			}
			catch (SocketException socketException)
			{
				VelNetLogger.Error("Socket exception: " + socketException);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error(ex.ToString());
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
		/// <param name="message">We can assume that this message is already formatted, so we just send it</param>
		/// <returns>True if the message successfully sent. False if it failed and we should quit</returns>
		private static bool SendTcpMessage(byte[] message)
		{
			if (instance.offlineMode)
			{
				instance.FakeServer(message);
				return true;
			}

			// Logging.Info("Sent: " + clientMessage);
			if (instance.socketConnection == null)
			{
				VelNetLogger.Error("Tried to send message while socket connection was still null.", instance);
				return false;
			}

			try
			{
				// check if we have been disconnected, if so shut down velnet
				if (!instance.socketConnection.Connected)
				{
					instance.DisconnectFromServer();
					VelNetLogger.Error("Disconnected from server. Most likely due to timeout.");
					return false;
				}

				// Get a stream object for writing. 			
				NetworkStream stream = instance.socketConnection.GetStream();
				if (stream.CanWrite)
				{
					stream.Write(message, 0, message.Length);
				}
			}
			catch (IOException ioException)
			{
				instance.DisconnectFromServer();
				VelNetLogger.Error("Disconnected from server. Most likely due to timeout.\n" + ioException);
				return false;
			}
			catch (SocketException socketException)
			{
				VelNetLogger.Error("Socket exception: " + socketException);
			}

			return true;
		}

		private static byte[] GetBigEndianBytes(int n)
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
			if (string.IsNullOrEmpty(roomName))
			{
				VelNetLogger.Error("Room name is null. Can't get info for this room.");
				return;
			}

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
		[Obsolete("Use JoinRoom() instead")]
		public static void Join(string roomName)
		{
			JoinRoom(roomName);
		}

		/// <summary>
		/// Joins a room by name
		/// </summary>
		/// <param name="roomName">The name of the room to join</param>
		public static void JoinRoom(string roomName)
		{
			if (instance.userid == -1)
			{
				Debug.LogError("Joining room before logging in.", instance);
				return;
			}

			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);

			byte[] roomBytes = Encoding.UTF8.GetBytes(roomName);
			writer.Write((byte)MessageSendType.MESSAGE_JOINROOM);
			writer.Write((byte)roomBytes.Length);
			writer.Write(roomBytes);
			SendTcpMessage(stream.ToArray());
		}


		/// <summary>
		/// Leaves a room if we're in one
		/// </summary>
		[Obsolete("Use LeaveRoom() instead")]
		public static void Leave()
		{
			LeaveRoom();
		}


		/// <summary>
		/// Leaves a room if we're in one
		/// </summary>
		public static void LeaveRoom()
		{
			if (InRoom)
			{
				JoinRoom(""); // super secret way to leave
			}
		}

		public static void SendCustomMessage(byte[] message, bool include_self = false, bool reliable = true,
			bool ordered = false)
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

		internal static bool SendToRoom(byte[] message, bool include_self = false, bool reliable = true,
			bool ordered = false)
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
				writer.WriteBigEndian(message.Length);
				writer.Write(message);
				return SendTcpMessage(mem.ToArray());
			}
			else
			{
				//udp message needs the type
				toSend[0] = sendType; //we don't 
				Array.Copy(GetBigEndianBytes(instance.userid), 0, toSend, 1, 4);
				Array.Copy(message, 0, toSend, 5, message.Length);
				SendUdpMessage(toSend, message.Length + 5); //shouldn't be over 1024...
				return true;
			}
		}


		internal static bool SendToGroup(string group, byte[] message, bool reliable = true)
		{
			byte[] utf8bytes = Encoding.UTF8.GetBytes(group);
			if (reliable)
			{
				MemoryStream stream = new MemoryStream();
				BinaryWriter writer = new BinaryWriter(stream);
				writer.Write((byte)MessageSendType.MESSAGE_GROUP);
				writer.WriteBigEndian(message.Length);
				writer.Write(message);
				writer.Write((byte)utf8bytes.Length);
				writer.Write(utf8bytes);
				return SendTcpMessage(stream.ToArray());
			}
			else
			{
				toSend[0] = (byte)MessageSendType.MESSAGE_GROUP;
				Array.Copy(GetBigEndianBytes(instance.userid), 0, toSend, 1, 4);
				//also need to send the group
				toSend[5] = (byte)utf8bytes.Length;
				Array.Copy(utf8bytes, 0, toSend, 6, utf8bytes.Length);
				Array.Copy(message, 0, toSend, 6 + utf8bytes.Length, message.Length);
				SendUdpMessage(toSend, 6 + utf8bytes.Length + message.Length);
				return true;
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
			byte[] groupNameBytes = Encoding.UTF8.GetBytes(groupName);
			writer.Write((byte)6);
			writer.Write((byte)groupNameBytes.Length);
			writer.Write(groupNameBytes);
			writer.WriteBigEndian(clientIds.Count * 4);
			foreach (int c in clientIds)
			{
				writer.WriteBigEndian(c);
			}

			SendTcpMessage(stream.ToArray());
		}

		[Obsolete("Use NetworkInstantiate instead. This matches the naming convention of NetworkDestroy")]
		public static NetworkObject InstantiateNetworkObject(string prefabName)
		{
			return NetworkInstantiate(prefabName);
		}

		public static NetworkObject NetworkInstantiate(NetworkObject prefab)
		{
			if (instance.prefabs.Contains(prefab))
			{
				return NetworkInstantiate(prefab.name);
			}

			throw new ArgumentException("Can't instantiate object that isn't added to the network manager.");
		}

		/// <summary>
		/// Instantiates a prefab for all players.
		/// </summary>
		/// <param name="prefabName">This prefab *must* by added to the list of prefabs in the scene's VelNetManager for all players.</param>
		/// <returns>The NetworkObject for the instantiated object.</returns>
		public static NetworkObject NetworkInstantiate(string prefabName)
		{
			VelNetPlayer owner = LocalPlayer;
			string networkId = AllocateNetworkId();
			if (instance.objects.ContainsKey(networkId))
			{
				VelNetLogger.Error("Can't instantiate object. Obj with that network ID was already instantiated.",
					instance.objects[networkId]);
				return null;
			}

			NetworkObject newObject = ActuallyInstantiate(networkId, prefabName, owner);

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.Instantiate);
			writer.Write(newObject.networkId);
			writer.Write(prefabName);
			SendToRoom(mem.ToArray(), include_self: false, reliable: true);

			return newObject;
		}

		/// <summary>
		/// Instantiates a prefab for all players at a specific location
		/// </summary>
		/// <param name="prefabName">This prefab *must* by added to the list of prefabs in the scene's VelNetManager for all players.</param>
		/// <param name="position"></param>
		/// <param name="rotation"></param>
		/// <returns>The NetworkObject for the instantiated object.</returns>
		public static NetworkObject NetworkInstantiate(string prefabName, Vector3 position, Quaternion rotation)
		{
			VelNetPlayer owner = LocalPlayer;
			string networkId = AllocateNetworkId();
			if (instance.objects.ContainsKey(networkId))
			{
				VelNetLogger.Error("Can't instantiate object. Obj with that network ID was already instantiated.",
					instance.objects[networkId]);
				return null;
			}

			NetworkObject newObject = ActuallyInstantiate(networkId, prefabName, owner, position, rotation);

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.InstantiateWithTransform);
			writer.Write(newObject.networkId);
			writer.Write(prefabName);
			writer.Write(position);
			writer.Write(rotation);
			SendToRoom(mem.ToArray(), include_self: false, reliable: true);

			return newObject;
		}

		/// <summary>
		/// Instantiates a prefab for all players at a specific location
		/// </summary>
		/// <param name="prefabName">This prefab *must* by added to the list of prefabs in the scene's VelNetManager for all players.</param>
		/// <param name="initialState">See NetworkObject.PackState for implementation format</param>
		/// <returns>The NetworkObject for the instantiated object.</returns>
		public static NetworkObject NetworkInstantiate(string prefabName, byte[] initialState)
		{
			VelNetPlayer owner = LocalPlayer;
			string networkId = AllocateNetworkId();
			if (instance.objects.ContainsKey(networkId))
			{
				VelNetLogger.Error("Can't instantiate object. Obj with that network ID was already instantiated.",
					instance.objects[networkId]);
				return null;
			}

			NetworkObject newObject = ActuallyInstantiate(networkId, prefabName, owner);
			
			using MemoryStream initialStateMem = new MemoryStream(initialState);
			using BinaryReader reader = new BinaryReader(initialStateMem);
			newObject.UnpackState(reader);

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.InstantiateWithState);
			writer.Write(newObject.networkId);
			writer.Write(prefabName);
			writer.Write(initialState);
			SendToRoom(mem.ToArray(), include_self: false, reliable: true);

			return newObject;
		}

		/// <summary>
		/// This happens locally on all clients
		/// </summary>
		/// <param name="networkId"></param>
		/// <param name="prefabName"></param>
		/// <param name="owner"></param>
		/// <returns></returns>
		internal static NetworkObject ActuallyInstantiate(string networkId, string prefabName, VelNetPlayer owner)
		{
			NetworkObject prefab = instance.prefabs.Find(p => p.name == prefabName);
			if (prefab == null)
			{
				VelNetLogger.Error("Couldn't find a prefab with that name: " + prefabName +
				                   "\nMake sure to add the prefab to list of prefabs in VelNetManager");
				return null;
			}

			NetworkObject newObject = Instantiate(prefab);
			newObject.networkId = networkId;
			newObject.prefabName = prefabName;
			newObject.owner = owner;
			try
			{
				newObject.OwnershipChanged?.Invoke(owner);
			}
			catch (Exception e)
			{
				VelNetLogger.Error("Error in event handling.\n" + e);
			}

			instance.objects.Add(newObject.networkId, newObject);
			return newObject;
		}


		internal static NetworkObject ActuallyInstantiate(string networkId, string prefabName, VelNetPlayer owner,
			Vector3 position, Quaternion rotation)
		{
			NetworkObject prefab = instance.prefabs.Find(p => p.name == prefabName);
			if (prefab == null)
			{
				VelNetLogger.Error("Couldn't find a prefab with that name: " + prefabName +
				                   "\nMake sure to add the prefab to list of prefabs in VelNetManager");
				return null;
			}

			NetworkObject newObject = Instantiate(prefab, position, rotation);
			newObject.instantiatedWithTransform = true;
			newObject.initialPosition = position;
			newObject.initialRotation = rotation;
			newObject.networkId = networkId;
			newObject.prefabName = prefabName;
			newObject.owner = owner;
			try
			{
				newObject.OwnershipChanged?.Invoke(owner);
			}
			catch (Exception e)
			{
				VelNetLogger.Error("Error in event handling.\n" + e);
			}

			instance.objects.Add(newObject.networkId, newObject);
			return newObject;
		}

		private static string AllocateNetworkId()
		{
			return LocalPlayer.userid + "-" + LocalPlayer.lastObjectId++;
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
				VelNetLogger.Error("Object to delete was already null");
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
				// VelNetLogger.Error("Object to delete was already null");
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
			if (!InRoom)
			{
				VelNetLogger.Error("Can't take ownership. Not in a room.");
				return false;
			}

			// local player must exist
			if (LocalPlayer == null)
			{
				VelNetLogger.Error("Can't take ownership. No local player.");
				return false;
			}

			// obj must exist
			if (!instance.objects.ContainsKey(networkId))
			{
				VelNetLogger.Error("Can't take ownership. Object with that network id doesn't exist: " + networkId);
				return false;
			}

			// if the ownership is locked, fail
			if (instance.objects[networkId].ownershipLocked)
			{
				VelNetLogger.Error("Can't take ownership. Ownership for this object is locked.");
				return false;
			}

			// immediately successful
			instance.objects[networkId].owner = LocalPlayer;
			try
			{
				instance.objects[networkId].OwnershipChanged?.Invoke(LocalPlayer);
			}
			catch (Exception e)
			{
				VelNetLogger.Error("Error in event handling.\n" + e);
			}

			// must be ordered, so that ownership transfers are not confused.
			// Also sent to all players, so that multiple simultaneous requests will result in the same outcome.
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);
			writer.Write((byte)MessageType.TakeOwnership);
			writer.Write(networkId);
			SendToRoom(mem.ToArray(), false, true);

			return true;
		}

		/// <summary>
		/// Pretends to be the server and sends back the result to be handled. Only should be called in offline mode.
		/// </summary>
		/// <param name="message">The message that was meant to be sent to the server.</param>
		private void FakeServer(byte[] message)
		{
			MemoryStream mem = new MemoryStream(message);
			BinaryReader reader = new BinaryReader(mem);
			MessageSendType messageType = (MessageSendType)reader.ReadByte();

			MemoryStream outMem = new MemoryStream();
			BinaryWriter outWriter = new BinaryWriter(outMem);

			switch (messageType)
			{
				case MessageSendType.MESSAGE_OTHERS_ORDERED:
					break;
				case MessageSendType.MESSAGE_ALL_ORDERED:
					break;
				case MessageSendType.MESSAGE_LOGIN:
					outWriter.Write((byte)MessageReceivedType.LOGGED_IN);
					outWriter.WriteBigEndian(0);
					break;
				case MessageSendType.MESSAGE_GETROOMS:
					outWriter.Write((byte)MessageReceivedType.ROOM_LIST);
					outWriter.WriteBigEndian(0);
					break;
				case MessageSendType.MESSAGE_JOINROOM:
				{
					string roomName = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadByte()));

					// if leaving a room
					if (string.IsNullOrEmpty(roomName))
					{
						outWriter.Write((byte)MessageReceivedType.YOU_LEFT);
						byte[] roomNameBytes = Encoding.UTF8.GetBytes(offlineRoomName);
						outWriter.Write((byte)roomNameBytes.Length);
						outWriter.Write(roomNameBytes);
					}
					else
					{
						outWriter.Write((byte)MessageReceivedType.YOU_JOINED);
						outWriter.WriteBigEndian(1); // num players
						outWriter.WriteBigEndian(0); // our userid
						byte[] roomNameBytes = Encoding.UTF8.GetBytes(roomName);
						outWriter.Write((byte)roomNameBytes.Length);
						outWriter.Write(roomNameBytes);
					}

					offlineRoomName = roomName;
					break;
				}
				case MessageSendType.MESSAGE_OTHERS:
					break;
				case MessageSendType.MESSAGE_ALL:
					break;
				case MessageSendType.MESSAGE_GROUP:
					break;
				case MessageSendType.MESSAGE_SETGROUP:
					break;
				case MessageSendType.MESSAGE_GETROOMDATA:
				{
					outWriter.Write((byte)MessageReceivedType.ROOM_DATA);
					byte[] roomNameBytes = Encoding.UTF8.GetBytes("OFFLINE");
					outWriter.Write((byte)roomNameBytes.Length);
					outWriter.Write(roomNameBytes);
					outWriter.WriteBigEndian(0);
					break;
				}
				default:
					throw new ArgumentOutOfRangeException();
			}

			outMem.Position = 0;
			// if we run this in the same thread, then it is modifying the messages collection while iterating
			Task.Run(() => { HandleIncomingMessage(new BinaryReader(outMem)); });
		}
	}

	public static partial class BinaryWriterExtensions
	{
		public static void WriteBigEndian(this BinaryWriter writer, int value)
		{
			byte[] data = BitConverter.GetBytes(value);
			Array.Reverse(data);
			writer.Write(data);
		}

		public static int ReadBigEndian(this BinaryReader reader)
		{
			byte[] data = reader.ReadBytes(4);
			Array.Reverse(data);
			return BitConverter.ToInt32(data, 0);
		}
	}
}