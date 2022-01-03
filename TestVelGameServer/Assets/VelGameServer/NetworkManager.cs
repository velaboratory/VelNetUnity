using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Dissonance;
public class NetworkManager : MonoBehaviour
{
	public string host;
	public int port;
	#region private members 	
	private TcpClient socketConnection;
	private Thread clientReceiveThread;
	public int userid = -1;
	public string room;
	int messagesReceived = 0;
	public GameObject playerPrefab;
	public Dictionary<int, NetworkPlayer> players = new Dictionary<int, NetworkPlayer>();

	public Action<NetworkPlayer> onJoinedRoom = delegate { };
	public Action<NetworkPlayer> onPlayerJoined = delegate { };
	public Action<NetworkPlayer> onPlayerLeft = delegate { };

	public List<NetworkObject> prefabs = new List<NetworkObject>();
	NetworkObject[] sceneObjects;
	public Dictionary<string, NetworkObject> objects = new Dictionary<string, NetworkObject>(); //maintains a list of all known objects on the server (ones that have ids)
	NetworkPlayer masterPlayer = null;
	#endregion
	// Use this for initialization
	public class Message
    {
		public int type;
		public string text;
		public int sender;
    }
	public List<Message> receivedMessages = new List<Message>();
	void Start()
	{
		ConnectToTcpServer();
		sceneObjects = GameObject.FindObjectsOfType<NetworkObject>(); //add all local network objects

	}

	

	private void addMessage(Message m)
	{
		lock (receivedMessages)
		{
			//Debug.Log(messagesReceived++);
			receivedMessages.Add(m);
		}
	}
    private void Update() 
    {
        lock(receivedMessages) { //the main thread, which can do Unity stuff
			foreach(Message m in receivedMessages)
            {
				if(m.type == 0) //when you join the server
                {
					this.userid = m.sender;
					Debug.Log("joined server");
                }
				 
				if (m.type == 2)
				{
					//if this message is for me, that means I joined a new room...
					if (this.userid == m.sender)
					{
						foreach (KeyValuePair<int, NetworkPlayer> kvp in players)
						{
							Destroy(kvp.Value.gameObject);
						}
						players.Clear(); //we clear the list, but will recreate as we get messages from people in our room

						if (m.text != "")
						{
							NetworkPlayer player = GameObject.Instantiate<GameObject>(playerPrefab).GetComponent<NetworkPlayer>();
							
							player.isLocal = true;
							player.userid = m.sender;
							players.Add(userid, player);
							player.room = m.text;
							player.manager = this;
							onJoinedRoom(player);
						}
					}
					else //not for me, a player is joining or leaving
					{
						NetworkPlayer me = players[userid];

						if (me.room != m.text)
						{
							//we got a left message, kill it
							Destroy(players[m.sender].gameObject);
							players.Remove(m.sender);
						}
						else
						{
							//we got a join mesage, create it
							NetworkPlayer player = GameObject.Instantiate<GameObject>(playerPrefab).GetComponent<NetworkPlayer>();
							player.isLocal = false;
							player.room = m.text;
							player.userid = m.sender;
							player.manager = this;
							players.Add(m.sender, player);
							onPlayerJoined(player);
						}
					}
				}
				if(m.type == 3) //generic message
                {

					players[m.sender]?.handleMessage(m);
                    
                }
				if(m.type == 4) //change master player (this should only happen when the first player joins or if the master player leaves)
                {
					if (masterPlayer == null)
					{
						masterPlayer = players[m.sender];
						
						//no master player yet, add the scene objects
						
						for (int i = 0; i < sceneObjects.Length; i++)
						{
							sceneObjects[i].networkId = -1 + "-" + i;
							sceneObjects[i].owner = masterPlayer;
							objects.Add(sceneObjects[i].networkId,sceneObjects[i]);
						}
						
					}
                    else
                    {
						masterPlayer = players[m.sender];
                    }

					masterPlayer.setAsMasterPlayer();

					//master player should take over any objects that do not have an owner

					foreach(KeyValuePair<string,NetworkObject> kvp in objects)
                    {
						kvp.Value.owner = masterPlayer;
                    }

				}
				
				messageReceived(m);
            }
			receivedMessages.Clear();
        }
    }
    private void OnApplicationQuit()
    {
		socketConnection.Close();
    }

    public Action<string, int> joinedRoom = delegate { };
	public Action<Message> messageReceived = delegate { };
	public Action<string, int> loggedIn = delegate { };
	public Action<string[], int> roomsReceived = delegate { };

	public bool connected = false;
	/// <summary> 	
	/// Setup socket connection. 	
	/// </summary> 	
	private void ConnectToTcpServer()
	{
		try
		{
			clientReceiveThread = new Thread(new ThreadStart(ListenForData));
			clientReceiveThread.IsBackground = true;
			clientReceiveThread.Start();
		}
		catch (Exception e)
		{
			Debug.Log("On client connect exception " + e);
		}
	}
	void handleMessage(string s) //this parses messages from the server, and adds them to a queue to be processed on the main thread
    {
		Message m = new Message();
		string[] sections = s.Split(':');
		if (sections.Length > 0)
		{
			int type = int.Parse(sections[0]);

			switch (type)
			{
				case 0://logged in message
					{ 
						if(sections.Length > 1)
                        {
							
							m.type = type;
							m.sender = int.Parse(sections[1]);
							m.text = "";
							addMessage(m);
                        }
						break;
					}
				case 1://room info message
					{

						
						break;
					}
				case 2: //joined room message
					{
						if(sections.Length > 2)
                        {
							m.type = 2;
							int user_id = int.Parse(sections[1]);
							m.sender = user_id;
							string new_room = sections[2];
							m.text = new_room;

							addMessage(m);
                        }
						break;
					}
				case 3: //text message
					{
						if(sections.Length > 2)
                        {
							m.type = 3;
							m.sender = int.Parse(sections[1]);
							m.text = sections[2];
							addMessage(m);
                        }
						break;
					}
				case 4: //change master client
                    {
						if(sections.Length > 1)
                        {
							m.type = 4;
							m.sender = int.Parse(sections[1]);
							addMessage(m);
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
			Byte[] bytes = new Byte[1024];
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
						var incommingData = new byte[length];
						Array.Copy(bytes, 0, incommingData, 0, length);
						// Convert byte array to string message. 						
						string serverMessage = Encoding.ASCII.GetString(incommingData);
						string[] sections = serverMessage.Split('\n');
						if(sections.Length > 1)
                        {
                            lock(receivedMessages){
								for (int i = 0; i < sections.Length - 1; i++)
								{
                                    if (i == 0)
                                    {
										handleMessage(partialMessage + sections[0]);
										partialMessage = "";
                                    }
                                    else
                                    {
										handleMessage(sections[i]);
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

	public void login(string username, string password)
    {
		SendNetworkMessage("0:" + username + ":" + password);
    }
	public void join(string roomname)
    {
		SendNetworkMessage("2:" + roomname);
    }
	public void leave()
    {
		SendNetworkMessage("2:-1\n");
    }
	public void sendTo(int type, string message)
    {
		SendNetworkMessage("3:" + type + ":" + message);
    }
}