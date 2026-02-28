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
using System.IO;
using System.Threading.Tasks;
using NativeWebSocket;
using System.Collections.Concurrent;
using UnityEngine.Profiling; // Ensure you have this library

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
			MESSAGE_LOGIN = 0,
			MESSAGE_GETROOMS = 1,
			MESSAGE_JOINROOM = 2,
			MESSAGE_OTHERS = 3,
			MESSAGE_ALL = 4,
			MESSAGE_GROUP = 5,
			MESSAGE_SETGROUP = 6,
			MESSAGE_OTHERS_ORDERED = 7,
			MESSAGE_ALL_ORDERED = 8,
			MESSAGE_GETROOMDATA = 9,
			MESSAGE_SELF = 10,
		};

		/// <summary>
		/// These messages are all handled by VelNetPlayer and are contained within DATA_MESSAGE
		/// </summary>
		public enum MessageType : byte
		{
			ObjectSync,
			TakeOwnership,
			Instantiate,
			InstantiateWithTransform,
			InstantiateWithState,
			ForceState,
			Destroy,
			DeleteSceneObjects,
			Custom,
			KeepAlive,
		}

		private const int serverCompatibleVersion = 2;
		public string host;
		public int port;

		public static VelNetManager instance;

		public int userid = -1;
		private TcpClient socketConnection;
		private Socket udpSocket;

		private WebSocket webSocket;
		private bool useWebSocket;

		private static int MAX_UDP_PACKET_SIZE = 65000; 
		private static int MAX_UDP_QUEUE_SIZE = 500; //will drop after this
		// The buffer for WebSocket stream accumulation
		private List<byte> wsBuffer = new List<byte>();

		// The result type for the new handler
		private enum MessageParseResult
		{
			Success,
			NeedMoreData, // Fragmentation happened
			Error         // Data corruption
		}

		public bool udpConnected;
		private IPEndPoint RemoteEndPoint;
		private Thread clientReceiveThread;
		private Thread clientReceiveThreadUDP;
		private Thread clientSendThreadUDP;
		public bool connected;
		private bool wasConnected;
		private double lastConnectionCheck;
		private double lastKeepAliveCheck;
		public float keepAliveInterval = 2f;
		public static int Ping { get; internal set; }
		public bool autoSwitchToOfflineMode;
		private bool offlineMode;
		private static bool inRoom;
		public static bool OfflineMode => instance?.offlineMode ?? false;
		private string offlineRoomName;

		[Tooltip("Sends debug messages about connection and join events")]
		public bool debugMessages;

		[Tooltip("Automatically logs in with app name and hash of device id after connecting")]
		public bool autoLogin = true;

		[Tooltip("Automatically try to reconnect if the server becomes disconnected")]
		public bool autoReconnect = true;

		/// <summary>
		/// Is the game currently trying to connect. Don't try to connect twice at once, because that breaks everything.
		/// </summary>
		private bool connecting;

		[Tooltip("Uses the game's version number in the login to prevent crosstalk between different app versions")]
		public bool onlyConnectToSameVersion;

		private static readonly Queue<Action> mainThreadExecutionQueue = new Queue<Action>();

		public readonly Dictionary<int, VelNetPlayer> players = new Dictionary<int, VelNetPlayer>();

		private struct UdpSendPacket
		{
			public byte[] Buffer;
			public int Length;
		}

		private static readonly ConcurrentQueue<UdpSendPacket> udpSendQueue = new ConcurrentQueue<UdpSendPacket>();
		private static readonly ConcurrentQueue<byte[]> udpBufferPool = new ConcurrentQueue<byte[]>();

		#region Callbacks

		/// <summary>
		/// We just joined a room
		/// string - the room name
		/// `VelNetManager.Players` is already populated when we get this message.
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

		/// <summary>
		/// We tried to connect to the server, but failed
		/// </summary>
		public static Action OnFailedToConnectToServer;

		/// <summary>
		/// We were previously successfully connected to a server, but we got disconnected
		/// </summary>
		public static Action OnDisconnectedFromServer;

		public static Action OnLoggedIn;
		public static Action<RoomsMessage> RoomsReceived;
		public static Action<RoomDataMessage> RoomDataReceived;

		public static Action<Message> MessageReceived;
		public static Action<int, byte[]> CustomMessageReceived;

		/// <summary>
		/// I just spawned a local network object
		/// </summary>
		public static Action<NetworkObject> OnLocalNetworkObjectSpawned;

		/// <summary>
		/// Anybody (including myself) just spawned a network object
		/// </summary>
		public static Action<NetworkObject> OnNetworkObjectSpawned;

		/// <summary>
		/// Called after a network object is destroyed
		/// string is the networkId of the destroyed object
		/// </summary>
		public static Action<string> OnNetworkObjectDestroyed;

		#endregion


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

public static VelNetPlayer LocalPlayer
{
	get
	{
		if (instance == null)
			return null;

		var players = instance.players.Values;
		int count = players.Count;
		VelNetPlayer[] array = new VelNetPlayer[count];
		players.CopyTo(array, 0);

		for (int i = 0; i < count; i++)
		{
			if (array[i].isLocal)
				return array[i];
		}

		return null;
	}
}

 public static bool InRoom => inRoom;
		public static string Room => LocalPlayer?.room;

		public static List<VelNetPlayer> Players => instance.players.Values.ToList();

		/// <summary>
		/// The player count in this room.
		/// -1 if not in a room.
		/// </summary>
		public static int PlayerCount => instance.players.Count;

		//public static bool IsConnected => instance != null && instance.connected && instance.udpConnected;
		//public static bool IsConnected => instance != null && instance.connected && (instance.udpConnected || instance.useWebSocket);
		public static bool IsConnected => instance != null && instance.connected;
		// this is for sending udp packets
		private static readonly byte[] toSend = new byte[MAX_UDP_PACKET_SIZE];

		// Shared reusable writers for building outgoing messages (main thread only)
		private static readonly NetworkWriter sendWriter = new NetworkWriter(1024);
		private static readonly NetworkWriter tempWriter = new NetworkWriter(256);
		// Small buffer for TCP transport header (sendType + big-endian length = 5 bytes)
		private static readonly byte[] tcpHeader = new byte[5];

		/// <summary>
		/// Returns the shared send NetworkWriter for building outgoing messages.
		/// Must only be used on the main thread. Caller must Reset() before use.
		/// </summary>
		internal static NetworkWriter GetSendWriter() => sendWriter;

		/// <summary>
		/// Returns a temporary NetworkWriter for intermediate serialization (e.g. PackState).
		/// Must only be used on the main thread. Caller must Reset() before use.
		/// </summary>
		internal static NetworkWriter GetTempWriter() => tempWriter;

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
			public int serverVersion;
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

		//private const int maxUnreadMessages = 1000;
		private readonly List<Message> receivedMessages = new List<Message>();

		private void Awake()
		{
			SceneManager.sceneLoaded += (_, _) =>
			{
				// add all local network objects
				sceneObjects = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None).Where(o => o.isSceneObject)
					.ToArray();
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
			//bool added = false;
			lock (receivedMessages)
			{
				// this is to avoid backups when headset goes to sleep
				//if (receivedMessages.Count < maxUnreadMessages)
				//{
					receivedMessages.Add(m);
				//	added = true;
				//}
			}

			try
			{
				//if (added) 
				MessageReceived?.Invoke(m);
			}
			catch (Exception e)
			{
				VelNetLogger.Error(e.ToString());
			}
		}

		// --- Safe Read Helpers for WebSockets ---

		private bool TryReadByte(BinaryReader reader, out byte result)
		{
			if (reader.BaseStream.Length - reader.BaseStream.Position < 1)
			{
				result = 0;
				return false;
			}
			result = reader.ReadByte();
			return true;
		}

		private bool TryReadBytes(BinaryReader reader, int count, out byte[] result)
		{
			if (reader.BaseStream.Length - reader.BaseStream.Position < count)
			{
				result = null;
				return false;
			}
			result = reader.ReadBytes(count);
			return true;
		}

		private bool TryReadInt(BinaryReader reader, out int result)
		{
			if (!TryReadBytes(reader, 4, out byte[] data))
			{
				result = 0;
				return false;
			}
			result = GetIntFromBytes(data);
			return true;
		}

		private void Update()
		{

			// --- ADD THIS BLOCK HERE ---
			if (useWebSocket && webSocket != null)
			{
				#if !UNITY_WEBGL || UNITY_EDITOR
				webSocket.DispatchMessageQueue();
				#endif
			}
			
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
							if (serverCompatibleVersion != lm.serverVersion)
							{
								VelNetLogger.Error(
									$"Server version mismatch. Please update the server.\nServer Version: {lm.serverVersion}\tClient Version: {serverCompatibleVersion}");
								DisconnectFromServer();
							}

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

							inRoom = true;

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
							if (players.TryGetValue(dm.senderId, out VelNetPlayer player))
							{
								player.HandleMessage(dm);
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
								if (players.TryGetValue(cm.masterId, out VelNetPlayer player))
								{
									masterPlayer = player;
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

			lock (mainThreadExecutionQueue)
			{
				while (mainThreadExecutionQueue.Count > 0)
				{
					mainThreadExecutionQueue.Dequeue().Invoke();
				}
			}

			if (IsConnected && Time.time - lastKeepAliveCheck > keepAliveInterval)
			{
				lastKeepAliveCheck = Time.time;
				sendWriter.Reset();
				sendWriter.Write((byte)MessageType.KeepAlive);
				sendWriter.Write(DateTime.UtcNow.ToBinary());
				SendToSelf(sendWriter.Buffer, 0, sendWriter.Length);
			}

			// reconnection
			if (autoReconnect && Time.timeAsDouble - lastConnectionCheck > 5)
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
			inRoom = false;
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
			clientSendThreadUDP?.Abort();
		}

		/// <summary>
		/// Setup socket connection.
		/// </summary>
		public static void ConnectToServer()
		{
			instance.StartCoroutine(instance.ConnectToServerCo());
		}

		private IEnumerator ConnectToServerCo()
		{
			if (IsConnected || instance.connecting)
			{
				Debug.LogError("Already connected to server!");
			}
			
			float maxWait = 5f;
			// wait for an existing connection to finish
			while (connecting && maxWait > 0)
			{
				Debug.LogError("Already connecting to server! Waiting...");
				maxWait -= Time.deltaTime;
				yield return null;
			}

			instance.connecting = true;

			try
			{
				instance.clientReceiveThread = new Thread(instance.ListenForData);
				instance.clientReceiveThread.Start();
			}
			catch (Exception e)
			{
				VelNetLogger.Error("On client connect exception " + e);
			}
			finally
			{
				instance.connecting = false;
			}
		}

		public static void SetServer(string host, int port)
		{
			if (instance.host == host && instance.port == port) return;
			instance.StartCoroutine(instance.SetServerCo(host, port));
		}

		private IEnumerator SetServerCo(string newHost, int newPort)
		{
			DisconnectFromServer();
			yield return null;
			instance.host = newHost;
			instance.port = newPort;
			ConnectToServer();
		}

		public static void DisconnectFromServer()
		{
			VelNetLogger.Info("Disconnecting from server...");
			string oldRoom = LocalPlayer?.room;
			
			// Delete all NetworkObjects that aren't scene objects or are null now
			// Null checks added for safety
			if (instance.objects != null)
			{
				instance.objects
					.Where(kvp => kvp.Value == null || !kvp.Value.isSceneObject)
					.Select(o => o.Key)
					.ToList().ForEach(SomebodyDestroyedNetworkObject);

				// Then remove references to the ones that are left
				instance.objects.Clear();
			}

			if (instance.groups != null)
			{
				instance.groups.Clear();
			}

			if (instance.sceneObjects != null)
			{
				foreach (NetworkObject s in instance.sceneObjects)
				{
					if (s != null) s.owner = null;
				}
			}

			if (instance.players != null)
			{
				instance.players.Clear();
			}
			instance.masterPlayer = null;


			// --- CONNECTION CLEANUP ---

			instance.connected = false;
			instance.udpConnected = false;
			instance.connecting = false;
			instance.useWebSocket = false; // Reset the fallback flag

			// Close TCP
			if (instance.socketConnection != null)
			{
				try { instance.socketConnection.Close(); } catch { }
				instance.socketConnection = null;
			}

			// Close WebSocket (NativeWebSocket)
			if (instance.webSocket != null)
			{
				// NativeWebSocket's Close is async, but usually fire-and-forget is fine for disconnects
				try { instance.webSocket.Close(); } catch { }
				instance.webSocket = null;
			}

			// Abort Threads
			if (instance.clientReceiveThreadUDP != null)
			{
				try { instance.clientReceiveThreadUDP.Abort(); } catch { }
				instance.clientReceiveThreadUDP = null;
			}

			if (instance.clientReceiveThread != null)
			{
				try { instance.clientReceiveThread.Abort(); } catch { }
				instance.clientReceiveThread = null;
			}

			instance.udpSocket = null;

			OnDisconnectedFromServer?.Invoke();
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
			// Big-endian to int without allocation
			return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
		}

		private static int ReadBigEndianInt(byte[] buffer, int offset)
		{
			return (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
		}

		private void ListenForData()
{
    try
    {
        useWebSocket = false;

        // 1. Try TCP First
        try
        {
            VelNetLogger.Info($"Connecting via TCP to {host}:{port}...");
            socketConnection = new TcpClient(host, port);
            socketConnection.NoDelay = true;
            
            // If TCP succeeds, we proceed to the blocking loop below
            connected = true;
            connecting = false;
        }
        catch (SocketException)
        {
            VelNetLogger.Error("TCP Connection failed. Attempting WebSocket fallback...");
            
            // 2. Fallback to NativeWebSocket
            useWebSocket = true;
            
            // Connect to wss://{host}/ 
            // NOTE: NativeWebSocket handles 'wss' vs 'ws' automatically based on the string
            string url = $"wss://{host}/"; 
            
            // Add custom headers if your websockify setup needs them, otherwise leave empty
            webSocket = new WebSocket(url);

            // --- Hook up Events ---
            
            webSocket.OnOpen += () => {
                VelNetLogger.Info("WebSocket Connected!");
                connected = true;
                connecting = false;
                
                // Manually fire the connected message since we aren't in the loop anymore
                lock (receivedMessages) {
                    receivedMessages.Add(new ConnectedMessage());
                }
            };

            webSocket.OnError += (e) => {
                VelNetLogger.Error("WebSocket Error: " + e);
                // Handle disconnection/failure
                lock (mainThreadExecutionQueue) {
                    mainThreadExecutionQueue.Enqueue(() => { OnFailedToConnectToServer?.Invoke(); });
                }
            };

            webSocket.OnClose += (e) => {
                VelNetLogger.Info("WebSocket Closed");
                lock (mainThreadExecutionQueue) {
                    mainThreadExecutionQueue.Enqueue(() => { OnDisconnectedFromServer?.Invoke(); });
                }
            };

			webSocket.OnMessage += (bytes) => {
				// 1. Add to buffer
				lock (wsBuffer) {
					wsBuffer.AddRange(bytes);
				}

				// 2. Process Loop
				while (true)
				{
					// Snapshot the buffer
					byte[] snapshot;
					lock (wsBuffer) {
						if (wsBuffer.Count == 0) return;
						snapshot = wsBuffer.ToArray();
					}

					using (MemoryStream ms = new MemoryStream(snapshot))
					using (BinaryReader reader = new BinaryReader(ms))
					{
						// Call the NEW handler
						MessageParseResult result = HandleBufferedMessage(reader);

						if (result == MessageParseResult.Success)
						{
							// Remove the bytes we successfully consumed
							int bytesConsumed = (int)ms.Position;
							lock (wsBuffer) {
								wsBuffer.RemoveRange(0, bytesConsumed);
							}
							// Loop continues to see if there is another message
						}
						else if (result == MessageParseResult.NeedMoreData)
						{
							// Stop processing and wait for more data from OnMessage
							break; 
						}
						else // Error
						{
							VelNetLogger.Error("Stream Desync. Clearing Buffer.");
							lock (wsBuffer) wsBuffer.Clear();
							break;
						}
					}
				}
			};

            // Start the connection (Async)
            webSocket.Connect();
            
            // CRITICAL: Exit this thread! 
            // NativeWebSocket runs in Update(), not in this background thread.
            return; 
        }

        // --- TCP BLOCKING LOOP (Legacy Path) ---
        // We only reach here if TCP connected successfully
        AddMessage(new ConnectedMessage());
        
        using (NetworkStream stream = socketConnection.GetStream())
        using (BinaryReader reader = new BinaryReader(stream))
        {
            while (socketConnection.Connected)
            {
                HandleIncomingMessage(reader);
            }
        }

        lock (mainThreadExecutionQueue)
        {
            mainThreadExecutionQueue.Enqueue(() => { OnDisconnectedFromServer?.Invoke(); });
        }
    }
    catch (Exception ex)
    {
        VelNetLogger.Error("Connection Exception: " + ex);
        lock (mainThreadExecutionQueue)
        {
            mainThreadExecutionQueue.Enqueue(() => { OnFailedToConnectToServer?.Invoke(); });
        }
    }
    finally
    {
        // Only clean up flags if we aren't using WebSocket (which stays alive async)
        if (!useWebSocket) {
            connecting = false;
            connected = false;
        }
    }
}


private MessageParseResult HandleBufferedMessage(BinaryReader reader)
{
    // 1. Try Read Type
    if (!TryReadByte(reader, out byte typeByte)) return MessageParseResult.NeedMoreData;
    
    MessageReceivedType type = (MessageReceivedType)typeByte;

    switch (type)
    {
        case MessageReceivedType.LOGGED_IN:
        {
            if (!TryReadInt(reader, out int uId)) return MessageParseResult.NeedMoreData;
            if (!TryReadInt(reader, out int ver)) return MessageParseResult.NeedMoreData;

            AddMessage(new LoginMessage { userId = uId, serverVersion = ver });
            return MessageParseResult.Success;
        }

        case MessageReceivedType.ROOM_LIST:
        {
            if (!TryReadInt(reader, out int n)) return MessageParseResult.NeedMoreData;
            if (!TryReadBytes(reader, n, out byte[] utf8data)) return MessageParseResult.NeedMoreData;

            RoomsMessage rm = new RoomsMessage { rooms = new List<ListedRoom>() };
            string roomMessage = Encoding.UTF8.GetString(utf8data);

            if (!string.IsNullOrEmpty(roomMessage))
            {
                foreach (string s in roomMessage.Split(','))
                {
                    string[] pieces = s.Split(':');
                    if (pieces.Length == 2)
                    {
                        rm.rooms.Add(new ListedRoom
                        {
                            name = pieces[0],
                            numUsers = int.Parse(pieces[1])
                        });
                    }
                }
            }
            AddMessage(rm);
            return MessageParseResult.Success;
        }

        case MessageReceivedType.ROOM_DATA:
        {
            if (!TryReadByte(reader, out byte nameLen)) return MessageParseResult.NeedMoreData;
            if (!TryReadBytes(reader, nameLen, out byte[] roomNameBytes)) return MessageParseResult.NeedMoreData;
            
            // Note: We construct the object, but if we fail later, we just won't call AddMessage
            // and the next attempt will reconstruct it. This is fine.
            RoomDataMessage rdm = new RoomDataMessage { room = Encoding.UTF8.GetString(roomNameBytes) };

            if (!TryReadInt(reader, out int clientCount)) return MessageParseResult.NeedMoreData;

            for (int i = 0; i < clientCount; i++)
            {
                if (!TryReadInt(reader, out int clientId)) return MessageParseResult.NeedMoreData;
                if (!TryReadByte(reader, out byte uNameLen)) return MessageParseResult.NeedMoreData;
                if (!TryReadBytes(reader, uNameLen, out byte[] uNameBytes)) return MessageParseResult.NeedMoreData;
                
                rdm.members.Add((clientId, Encoding.UTF8.GetString(uNameBytes)));
            }
            AddMessage(rdm);
            return MessageParseResult.Success;
        }

        case MessageReceivedType.PLAYER_JOINED:
        {
            if (!TryReadInt(reader, out int uId)) return MessageParseResult.NeedMoreData;
            if (!TryReadByte(reader, out byte n)) return MessageParseResult.NeedMoreData;
            if (!TryReadBytes(reader, n, out byte[] roomNameBytes)) return MessageParseResult.NeedMoreData;

            AddMessage(new JoinMessage 
            { 
                userId = uId, 
                room = Encoding.UTF8.GetString(roomNameBytes) 
            });
            return MessageParseResult.Success;
        }

        case MessageReceivedType.DATA_MESSAGE:
        {
            if (!TryReadInt(reader, out int senderId)) return MessageParseResult.NeedMoreData;
            if (!TryReadInt(reader, out int n)) return MessageParseResult.NeedMoreData;
            if (!TryReadBytes(reader, n, out byte[] data)) return MessageParseResult.NeedMoreData;

            AddMessage(new DataMessage { senderId = senderId, data = data });
            return MessageParseResult.Success;
        }

        case MessageReceivedType.MASTER_MESSAGE:
        {
            if (!TryReadInt(reader, out int masterId)) return MessageParseResult.NeedMoreData;
            AddMessage(new ChangeMasterMessage { masterId = masterId });
            return MessageParseResult.Success;
        }

        case MessageReceivedType.YOU_JOINED:
        {
            if (!TryReadInt(reader, out int count)) return MessageParseResult.NeedMoreData;
            
            YouJoinedMessage m = new YouJoinedMessage { playerIds = new List<int>() };
            
            // Pre-check for optimization (optional, but good for large lists)
            if (reader.BaseStream.Length - reader.BaseStream.Position < count * 4) return MessageParseResult.NeedMoreData;

            for (int i = 0; i < count; i++)
            {
                TryReadInt(reader, out int pid); 
                m.playerIds.Add(pid);
            }

            if (!TryReadByte(reader, out byte n)) return MessageParseResult.NeedMoreData;
            if (!TryReadBytes(reader, n, out byte[] roomBytes)) return MessageParseResult.NeedMoreData;
            
            m.room = Encoding.UTF8.GetString(roomBytes);
            AddMessage(m);
            return MessageParseResult.Success;
        }

        case MessageReceivedType.PLAYER_LEFT:
        {
            if (!TryReadInt(reader, out int uId)) return MessageParseResult.NeedMoreData;
            if (!TryReadByte(reader, out byte n)) return MessageParseResult.NeedMoreData;
            if (!TryReadBytes(reader, n, out byte[] roomBytes)) return MessageParseResult.NeedMoreData;

            AddMessage(new PlayerLeftMessage
            {
                userId = uId,
                room = Encoding.UTF8.GetString(roomBytes)
            });
            return MessageParseResult.Success;
        }

        case MessageReceivedType.YOU_LEFT:
        {
            if (!TryReadByte(reader, out byte n)) return MessageParseResult.NeedMoreData;
            if (!TryReadBytes(reader, n, out byte[] roomBytes)) return MessageParseResult.NeedMoreData;

            AddMessage(new YouLeftMessage
            {
                room = Encoding.UTF8.GetString(roomBytes)
            });
            return MessageParseResult.Success;
        }

        default:
            VelNetLogger.Error("Unknown message type: " + type);
            return MessageParseResult.Error;
    }
}


		private void HandleIncomingMessage(BinaryReader reader)
		{
			MessageReceivedType type = (MessageReceivedType)reader.ReadByte();
			switch (type)
			{
				//login
				case MessageReceivedType.LOGGED_IN:
				{
					try
					{
						AddMessage(new LoginMessage
						{
							// not really the sender...
							userId = GetIntFromBytes(reader.ReadBytes(4)),
							serverVersion = GetIntFromBytes(reader.ReadBytes(4)),
						});
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}
				//rooms
				case MessageReceivedType.ROOM_LIST:
				{
					try
					{
						RoomsMessage m = new RoomsMessage();
						m.rooms = new List<ListedRoom>();
						int n = GetIntFromBytes(reader.ReadBytes(4)); //the size of the payload
						byte[] utf8data = reader.ReadBytes(n);
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
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}
				case MessageReceivedType.ROOM_DATA:
				{
					try
					{
						RoomDataMessage rdm = new RoomDataMessage();
						int n = reader.ReadByte();
						byte[] utf8data = reader.ReadBytes(n); //the room name, encoded as utf-8
						string roomname = Encoding.UTF8.GetString(utf8data);

						n = GetIntFromBytes(reader.ReadBytes(4)); //the number of client datas to read
						rdm.room = roomname;
						for (int i = 0; i < n; i++)
						{
							// client id + short string
							int clientId = GetIntFromBytes(reader.ReadBytes(4));
							int s = reader.ReadByte(); //size of string
							utf8data = reader.ReadBytes(s); //the username
							string username = Encoding.UTF8.GetString(utf8data);
							rdm.members.Add((clientId, username));
						}

						AddMessage(rdm);
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}
				//joined
				case MessageReceivedType.PLAYER_JOINED:
				{
					try
					{
						JoinMessage m = new JoinMessage();
						m.userId = GetIntFromBytes(reader.ReadBytes(4));
						int n = reader.ReadByte();
						byte[] utf8data = reader.ReadBytes(n); //the room name, encoded as utf-8
						m.room = Encoding.UTF8.GetString(utf8data);
						AddMessage(m);
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}
				//data
				case MessageReceivedType.DATA_MESSAGE:
				{
					try
					{
						DataMessage m = new DataMessage();
						m.senderId = GetIntFromBytes(reader.ReadBytes(4));
						int n = GetIntFromBytes(reader.ReadBytes(4)); //the size of the payload
						m.data = reader.ReadBytes(n); //the message
						AddMessage(m);
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}
				// new master
				case MessageReceivedType.MASTER_MESSAGE:
				{
					try
					{
						ChangeMasterMessage m = new ChangeMasterMessage();
						m.masterId = GetIntFromBytes(reader.ReadBytes(4)); // sender is the new master
						AddMessage(m);
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}

				case MessageReceivedType.YOU_JOINED:
				{
					try
					{
						YouJoinedMessage m = new YouJoinedMessage();
						int n = GetIntFromBytes(reader.ReadBytes(4));
						m.playerIds = new List<int>();
						for (int i = 0; i < n; i++)
						{
							m.playerIds.Add(GetIntFromBytes(reader.ReadBytes(4)));
						}

						n = reader.ReadByte();
						byte[] utf8data = reader.ReadBytes(n); //the room name, encoded as utf-8
						m.room = Encoding.UTF8.GetString(utf8data);
						AddMessage(m);
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}
				case MessageReceivedType.PLAYER_LEFT:
				{
					try
					{
						PlayerLeftMessage m = new PlayerLeftMessage();
						m.userId = GetIntFromBytes(reader.ReadBytes(4));
						int n = reader.ReadByte();
						byte[] utf8data = reader.ReadBytes(n); //the room name, encoded as utf-8
						m.room = Encoding.UTF8.GetString(utf8data);
						AddMessage(m);
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}
				case MessageReceivedType.YOU_LEFT:
				{
					try
					{
						YouLeftMessage m = new YouLeftMessage();
						int n = reader.ReadByte();
						byte[] utf8data = reader.ReadBytes(n); //the room name, encoded as utf-8
						m.room = Encoding.UTF8.GetString(utf8data);
						AddMessage(m);
					}
					catch (EndOfStreamException e)
					{
						VelNetLogger.Error($"Error while handling {type} message:\n{e}");
					}

					break;
				}
				default:
					VelNetLogger.Error("Unknown message type");
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}
		}

		private void ListenForDataUDP()
{
    // 1. Immediate exit if using WebSockets or Offline
    if (offlineMode || useWebSocket) 
    {
        udpConnected = false;
        return;
    }

    try
    {
        IPAddress[] addresses = Dns.GetHostAddresses(host);
        RemoteEndPoint = new IPEndPoint(addresses[0], port);

        udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		//udpSocket.Blocking = false;
		udpSocket.SendBufferSize = 4*1024*1024;
        // Fix: Ignore ICMP Port Unreachable errors to prevent false positives on Windows
        // (SIO_UDP_CONNRESET = -1744830452)
        try { udpSocket.IOControl((IOControlCode)(-1744830452), new byte[] { 0 }, null); } catch { }
		udpSocket.DontFragment = true;
        udpConnected = false;
        byte[] buffer = new byte[1024];
        
        // 2. Handshake Loop
        while (true)
        {
            // Send Handshake
            buffer[0] = 0; // MessageType.LOGGED_IN (Connect)
            WriteBigEndianInt(buffer, 1, userid);
			udpSocket.Connect(RemoteEndPoint);
            udpSocket.Send(buffer, 5, SocketFlags.None);

            // Wait for response with timeout
            if (udpSocket.Poll(100000, SelectMode.SelectRead))
            {
                // Data is available, verify it!
                try 
                {
                    int numReceived = udpSocket.Receive(buffer);
                    
                    // 3. VALIDATION: Only connect if the message is actually a LOGGED_IN response
                    if (numReceived > 0 && buffer[0] == (byte)MessageReceivedType.LOGGED_IN)
                    {
                        udpConnected = true;
                        wasConnected = true;
                        VelNetLogger.Info("UDP Connection Established.");
                        break; // Valid connection, break the loop
                    }
                }
                catch (SocketException) 
                {
                    // This catches the ICMP Port Unreachable and just retries
                    // instead of crashing the thread and setting udpConnected=true
                }
            }
            
            // Wait before retrying handshake
            Thread.Sleep(100);
        }

				if (udpConnected)
				{
					clientSendThreadUDP = new Thread(SendUDPLoop);
					clientSendThreadUDP.Start();
				}

        // 4. Data Loop
        while (udpConnected)
        {
            try 
            {
                int numReceived = udpSocket.Receive(buffer);
                if (numReceived > 0)
                {
                     // Handle Data
                    switch ((MessageReceivedType)buffer[0])
                    {
                        case MessageReceivedType.DATA_MESSAGE:
                        {
                            DataMessage m = new DataMessage();
                            m.senderId = ReadBigEndianInt(buffer, 1);
                            byte[] messageBytes = new byte[numReceived - 5];
                            Array.Copy(buffer, 5, messageBytes, 0, messageBytes.Length);
                            m.data = messageBytes;
                            AddMessage(m);
                            break;
                        }
                    }
                }
            }
            catch (SocketException)
            {
                // Allow read timeouts or ICMP errors to simply be ignored 
                // without killing the thread immediately
            }
        }
    }
    catch (ThreadAbortException)
    {
        // pass
    }
    catch (Exception ex)
    {
        VelNetLogger.Error("UDP Thread Error: " + ex);
    }
    finally
    {
        udpConnected = false;
        if (udpSocket != null)
        {
            udpSocket.Close();
            udpSocket = null;
        }
    }
}

		private static void SendUdpMessage(byte[] message, int N)
		{
			if (instance.udpSocket == null || !instance.udpConnected)
			{
				return;
			}

			if(udpSendQueue.Count > MAX_UDP_QUEUE_SIZE)
			{
				return; // Backpressure: If we have too many messages queued, just drop this one to avoid OOM
			}
			
			if (!udpBufferPool.TryDequeue(out byte[] pooledBuffer) || pooledBuffer.Length < N)
			{
				pooledBuffer = new byte[Math.Max(1500, N)]; // Safe pre-allocation limit
			}

			Buffer.BlockCopy(message, 0, pooledBuffer, 0, N);
			udpSendQueue.Enqueue(new UdpSendPacket { Buffer = pooledBuffer, Length = N });
		}

		private static void SendUDPLoop()
		{
			Profiler.BeginThreadProfiling("VelNet", "UDP Send Loop");
			while (instance.udpConnected)
			{
				if (udpSendQueue.TryDequeue(out UdpSendPacket packet))
				{
					try
					{
						instance.udpSocket.Send(packet.Buffer, packet.Length, SocketFlags.None);
					}
					catch (SocketException)
					{
						// Allow send errors to be ignored (e.g. if the connection is lost)
					}
					finally
					{
						udpBufferPool.Enqueue(packet.Buffer);
					}
				}
				else
				{
					Thread.Sleep(3); // No messages to send, wait a bit before checking again
				}
			}
			Profiler.EndThreadProfiling();
		}

		/// <summary> 	
		/// Send message to server using socket connection.
		/// </summary>
		/// <param name="message">We can assume that this message is already formatted, so we just send it</param>
		/// <returns>True if the message successfully sent. False if it failed and we should quit</returns>
		private static bool SendTcpMessage(byte[] buffer, int offset, int length)
{
    if (instance.offlineMode)
    {
        // FakeServer needs a standalone byte[] for its MemoryStream-based parsing
        byte[] copy = new byte[length];
        Array.Copy(buffer, offset, copy, 0, length);
        instance.FakeServer(copy);
        return true;
    }

    // --- WEBSOCKET PATH ---
    if (instance.useWebSocket)
    {
        if (instance.webSocket != null && instance.webSocket.State == WebSocketState.Open)
        {
            // WebSocket.Send requires a byte[], so we must copy if not already a standalone array
            if (offset == 0 && length == buffer.Length)
            {
                instance.webSocket.Send(buffer);
            }
            else
            {
                byte[] copy = new byte[length];
                Array.Copy(buffer, offset, copy, 0, length);
                instance.webSocket.Send(copy);
            }
            return true;
        }
        return false;
    }

    // --- TCP PATH ---
    if (instance.socketConnection == null)
    {
        return false;
    }

    try
    {
        if (!instance.socketConnection.Connected)
        {
            DisconnectFromServer();
            return false;
        }

        NetworkStream stream = instance.socketConnection.GetStream();
        if (stream.CanWrite)
        {
            stream.Write(buffer, offset, length);
        }
    }
    catch (Exception)
    {
        return false;
    }

    return true;
}
		/// <summary>
		/// Sends a TCP message as two parts (header + body) to avoid concatenating into a single buffer.
		/// </summary>
		private static bool SendTcpMessageTwoPart(byte[] header, int headerOffset, int headerLength, byte[] body, int bodyOffset, int bodyLength)
		{
			if (instance.offlineMode)
			{
				// FakeServer expects the full message as one byte[]
				byte[] combined = new byte[headerLength + bodyLength];
				Array.Copy(header, headerOffset, combined, 0, headerLength);
				Array.Copy(body, bodyOffset, combined, headerLength, bodyLength);
				instance.FakeServer(combined);
				return true;
			}

			if (instance.useWebSocket)
			{
				if (instance.webSocket != null && instance.webSocket.State == WebSocketState.Open)
				{
					byte[] combined = new byte[headerLength + bodyLength];
					Array.Copy(header, headerOffset, combined, 0, headerLength);
					Array.Copy(body, bodyOffset, combined, headerLength, bodyLength);
					instance.webSocket.Send(combined);
					return true;
				}
				return false;
			}

			if (instance.socketConnection == null) return false;

			try
			{
				if (!instance.socketConnection.Connected)
				{
					DisconnectFromServer();
					return false;
				}

				NetworkStream stream = instance.socketConnection.GetStream();
				if (stream.CanWrite)
				{
					stream.Write(header, headerOffset, headerLength);
					stream.Write(body, bodyOffset, bodyLength);
				}
			}
			catch (Exception)
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Writes a big-endian int directly into a buffer at the specified offset. Zero-allocation.
		/// </summary>
		private static void WriteBigEndianInt(byte[] buffer, int offset, int value)
		{
			buffer[offset] = (byte)(value >> 24);
			buffer[offset + 1] = (byte)(value >> 16);
			buffer[offset + 2] = (byte)(value >> 8);
			buffer[offset + 3] = (byte)value;
		}

		/// <summary>
		/// Connects to the server for a particular app
		/// </summary>
		/// <param name="appName">A unique name for your app. Communication can only happen between clients with the same app name.</param>
		/// <param name="deviceId">Should be unique per device that connects. e.g. md5(deviceUniqueIdentifier)</param>
		public static void Login(string appName, string deviceId)
		{
			byte[] id = Encoding.UTF8.GetBytes(deviceId);
			byte[] app = Encoding.UTF8.GetBytes(appName);
			sendWriter.Reset();
			sendWriter.Write((byte)MessageSendType.MESSAGE_LOGIN);
			sendWriter.Write((byte)id.Length);
			sendWriter.Write(id);
			sendWriter.Write((byte)app.Length);
			sendWriter.Write(app);
			SendTcpMessage(sendWriter.Buffer, 0, sendWriter.Length);
		}

		public static void GetRooms(Action<RoomsMessage> callback = null)
		{
			sendWriter.Reset();
			sendWriter.Write((byte)MessageSendType.MESSAGE_GETROOMS);
			SendTcpMessage(sendWriter.Buffer, 0, sendWriter.Length);

			if (callback != null)
			{
				RoomsReceived += RoomsReceivedCallback;
			}

			return;

			void RoomsReceivedCallback(RoomsMessage msg)
			{
				// This might not be the actual message we sent, but they are all the same
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

			byte[] R = Encoding.UTF8.GetBytes(roomName);
			sendWriter.Reset();
			sendWriter.Write((byte)MessageSendType.MESSAGE_GETROOMDATA);
			sendWriter.Write((byte)R.Length);
			sendWriter.Write(R);
			SendTcpMessage(sendWriter.Buffer, 0, sendWriter.Length);
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

			byte[] roomBytes = Encoding.UTF8.GetBytes(roomName);
			sendWriter.Reset();
			sendWriter.Write((byte)MessageSendType.MESSAGE_JOINROOM);
			sendWriter.Write((byte)roomBytes.Length);
			sendWriter.Write(roomBytes);
			SendTcpMessage(sendWriter.Buffer, 0, sendWriter.Length);
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
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.Custom);
			sendWriter.Write(message.Length);
			sendWriter.Write(message);
			SendToRoom(sendWriter.Buffer, 0, sendWriter.Length, include_self, reliable, ordered);
		}

		public static void SendCustomMessageToSelf(byte[] message, bool reliable = true)
		{
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.Custom);
			sendWriter.Write(message.Length);
			sendWriter.Write(message);
			SendToSelf(sendWriter.Buffer, 0, sendWriter.Length, reliable);
		}

		public static void SendCustomMessageToGroup(string group, byte[] message, bool reliable = true)
		{
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.Custom);
			sendWriter.Write(message.Length);
			sendWriter.Write(message);
			SendToGroup(group, sendWriter.Buffer, 0, sendWriter.Length, reliable);
		}

		internal static bool SendToSelf(byte[] buffer, int offset, int length, bool reliable = true)
		{
			const byte sendType = (byte)MessageSendType.MESSAGE_SELF;

			if (reliable || !instance.udpConnected)
			{
				// TCP: write transport header + message body directly
				tcpHeader[0] = sendType;
				WriteBigEndianInt(tcpHeader, 1, length);
				// Write header then body as two writes to avoid copying
				return SendTcpMessageTwoPart(tcpHeader, 0, 5, buffer, offset, length);
			}
			else
			{
				// UDP: pack into static toSend buffer
				toSend[0] = sendType;
				WriteBigEndianInt(toSend, 1, instance.userid);
				Array.Copy(buffer, offset, toSend, 5, length);
				SendUdpMessage(toSend, length + 5);
				return true;
			}
		}

		internal static bool SendToRoom(byte[] buffer, int offset, int length, bool include_self = false, bool reliable = true, bool ordered = false)
		{
			byte sendType = (byte)MessageSendType.MESSAGE_OTHERS;
			if (include_self && ordered) sendType = (byte)MessageSendType.MESSAGE_ALL_ORDERED;
			if (include_self && !ordered) sendType = (byte)MessageSendType.MESSAGE_ALL;
			if (!include_self && ordered) sendType = (byte)MessageSendType.MESSAGE_OTHERS_ORDERED;

			if (reliable || !instance.udpConnected)
			{
				tcpHeader[0] = sendType;
				WriteBigEndianInt(tcpHeader, 1, length);
				return SendTcpMessageTwoPart(tcpHeader, 0, 5, buffer, offset, length);
			}
			else
			{
				toSend[0] = sendType;
				WriteBigEndianInt(toSend, 1, instance.userid);
				//Array.Copy(buffer, offset, toSend, 5, length);
				Buffer.BlockCopy(buffer, offset, toSend, 5, length);
				SendUdpMessage(toSend, length + 5);
				return true;
			}
		}


		internal static bool SendToGroup(string group, byte[] buffer, int offset, int length, bool reliable = true)
		{
			byte[] utf8bytes = Encoding.UTF8.GetBytes(group);

			if (reliable || !instance.udpConnected)
			{
				// TCP: build full message into sendWriter since SendToGroup has a more complex format
				// (message body + group name appended after)
				sendWriter.Reset();
				sendWriter.Write((byte)MessageSendType.MESSAGE_GROUP);
				sendWriter.WriteBigEndianInt(length);
				sendWriter.Write(buffer, offset, length);
				sendWriter.Write((byte)utf8bytes.Length);
				sendWriter.Write(utf8bytes);
				return SendTcpMessage(sendWriter.Buffer, 0, sendWriter.Length);
			}
			else
			{
				toSend[0] = (byte)MessageSendType.MESSAGE_GROUP;
				WriteBigEndianInt(toSend, 1, instance.userid);
				toSend[5] = (byte)utf8bytes.Length;
				Array.Copy(utf8bytes, 0, toSend, 6, utf8bytes.Length);
				Array.Copy(buffer, offset, toSend, 6 + utf8bytes.Length, length);
				SendUdpMessage(toSend, 6 + utf8bytes.Length + length);
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

			byte[] groupNameBytes = Encoding.UTF8.GetBytes(groupName);
			sendWriter.Reset();
			sendWriter.Write((byte)6);
			sendWriter.Write((byte)groupNameBytes.Length);
			sendWriter.Write(groupNameBytes);
			sendWriter.WriteBigEndianInt(clientIds.Count * 4);
			foreach (int c in clientIds)
			{
				sendWriter.WriteBigEndianInt(c);
			}
			SendTcpMessage(sendWriter.Buffer, 0, sendWriter.Length);
		}

		[Obsolete("Use NetworkInstantiate instead. This matches the naming convention of NetworkDestroy")]
		public static NetworkObject InstantiateNetworkObject(string prefabName)
		{
			return NetworkInstantiate(prefabName);
		}

		public static NetworkObject NetworkInstantiate(NetworkObject prefab)
		{
			if (instance.prefabs.Find(p => p.name == prefab.name) != null)
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

			try
			{
				OnLocalNetworkObjectSpawned?.Invoke(newObject);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error("Error in event handling.\n" + ex);
			}

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.Instantiate);
			sendWriter.Write(newObject.networkId);
			sendWriter.Write(prefabName);
			SendToRoom(sendWriter.Buffer, 0, sendWriter.Length, include_self: false, reliable: true);

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

			try
			{
				OnLocalNetworkObjectSpawned?.Invoke(newObject);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error("Error in event handling.\n" + ex);
			}

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.InstantiateWithTransform);
			sendWriter.Write(newObject.networkId);
			sendWriter.Write(prefabName);
			sendWriter.Write(position);
			sendWriter.Write(rotation);
			SendToRoom(sendWriter.Buffer, 0, sendWriter.Length, include_self: false, reliable: true);

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

			try
			{
				OnLocalNetworkObjectSpawned?.Invoke(newObject);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error("Error in event handling.\n" + ex);
			}

			NetworkReader stateReader = new NetworkReader(initialState);
			newObject.UnpackState(stateReader);

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.InstantiateWithState);
			sendWriter.Write(newObject.networkId);
			sendWriter.Write(prefabName);
			sendWriter.Write(initialState);
			SendToRoom(sendWriter.Buffer, 0, sendWriter.Length, include_self: false, reliable: true);

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

			try
			{
				OnNetworkObjectSpawned?.Invoke(newObject);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error("Error in event handling.\n" + ex);
			}

			return newObject;
		}

		//this function is used to allow you to configure the new object after it is instantiated locally, but before it is packed up and sent to others to instantiate
		public static NetworkObject NetworkInstantiate(string prefabName, Action<NetworkObject> populateBeforePack)
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

			try
			{
				OnLocalNetworkObjectSpawned?.Invoke(newObject);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error("Error in event handling.\n" + ex);
			}

			populateBeforePack?.Invoke(newObject);

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.InstantiateWithState);
			sendWriter.Write(newObject.networkId);
			sendWriter.Write(prefabName);
			newObject.PackState(sendWriter);
			SendToRoom(sendWriter.Buffer, 0, sendWriter.Length, include_self: false, reliable: true);

			return newObject;
		}

		//this function helps w/ the case where we are instantiating from an existing reading, such as where we serialized all objects to a big byte array, and don't want to create a special reader just for those bytes
		public static NetworkObject NetworkInstantiate(string prefabName, NetworkReader reader)
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

			try
			{
				OnLocalNetworkObjectSpawned?.Invoke(newObject);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error("Error in event handling.\n" + ex);
			}

			newObject.UnpackState(reader);

			// only sent to others, as I already instantiated this.  Nice that it happens immediately.
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.InstantiateWithState);
			sendWriter.Write(newObject.networkId);
			sendWriter.Write(prefabName);
			newObject.PackState(sendWriter);
			SendToRoom(sendWriter.Buffer, 0, sendWriter.Length, include_self: false, reliable: true);

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

			try
			{
				OnNetworkObjectSpawned?.Invoke(newObject);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error("Error in event handling.\n" + ex);
			}

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
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.Destroy);
			sendWriter.Write(networkId);
			SendToRoom(sendWriter.Buffer, 0, sendWriter.Length, include_self: false, reliable: true);
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

			try
			{
				OnNetworkObjectDestroyed?.Invoke(networkId);
			}
			catch (Exception ex)
			{
				VelNetLogger.Error("Error in event handling.\n" + ex);
			}
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
			sendWriter.Reset();
			sendWriter.Write((byte)MessageType.TakeOwnership);
			sendWriter.Write(networkId);
			SendToRoom(sendWriter.Buffer, 0, sendWriter.Length, false, true);

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
			writer.Write((byte)(value >> 24));
			writer.Write((byte)(value >> 16));
			writer.Write((byte)(value >> 8));
			writer.Write((byte)value);
		}

		public static int ReadBigEndian(this BinaryReader reader)
		{
			byte b0 = reader.ReadByte();
			byte b1 = reader.ReadByte();
			byte b2 = reader.ReadByte();
			byte b3 = reader.ReadByte();
			return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
		}
	}

	
}
