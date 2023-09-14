VelNet uses a single-byte header on all messages that denotes the message type. The set of message types for sent messages is different from the set used when receiving messages.
It is generally not necessary to know or modify these internal data types.

!!! note

    The server encodes ints with a Big-Endian format, but C# uses Little-Endian on almost all platforms. Use the `BinaryWriter.WriteBigEndian()` extensions to encode in Big-Endian on the C# side.

## MessageSentType
```cs
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
```
### MESSAGE_LOGIN = 0
This message contains:
 - `byte` - The length of the deviceId string
 - `byte[]` - UTF8 encoding of the deviceId string
 - `byte` - The length of the appName string
 - `byte[]` - UTF8 encoding of the appName string
### MESSAGE_GETROOMS = 1
This message contains no data, just the single-byte header.
### MESSAGE_JOINROOM = 2
This message contains:
 - `byte` - The length of the roomName string
 - `byte[]` - UTF8 encoding of the roomName string
### MESSAGE_OTHERS = 3
### MESSAGE_ALL = 4
### MESSAGE_GROUP = 5
### MESSAGE_SETGROUP = 6
### MESSAGE_OTHERS_ORDERED = 7
### MESSAGE_ALL_ORDERED = 8
### MESSAGE_GETROOMDATA = 9

## MessageReceivedType
```cs
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
```
### LOGGED_IN = 0
This message contains:
 - `int` - The userid given by the server to this user.
### ROOM_LIST = 1
This message contains:
 - `int` - The length of the room mesesage string. This is an int instead of a byte because of the possible string length with many rooms.
 - `byte[]` - A UTF8-encoded string for the room message
   - This string is encoded as a comma-separated list of rooms, with the format `name:numUsers` for each room.
   - e.g. `Common_1:0:3,Auditorium_123:0,room1:10`
### PLAYER_JOINED = 2
This message contains:
 - `int` - The userid of the player that joined
 - `byte` - The length of the room name string
 - `byte[]` - A UTF8-encoded string for the room name
### DATA_MESSAGE = 3
This message contains:
 - `int` - The userid of the player that sent this message
 - `int` - The size of the payload
 - `byte[]` - The message data
   - Decoding the actual message data is handled in `VelNetPlayer.cs`
   - Within DATA_MESSAGE messages, there is an additonal type header:
     ```cs
     public enum MessageType : byte
     {
         ObjectSync,
         TakeOwnership,
         Instantiate,
         InstantiateWithTransform,
         Destroy,
         DeleteSceneObjects,
         Custom
     }
     ```
### MASTER_MESSAGE = 4
This message contains:
 - `int` - The new master client id. The sender of this message is the master.
### YOU_JOINED = 5
This is returned after you join a room.
This message contains:
 - `int` - The number of players in the room
 - For each player:
   - `int` - The player's userid
 - `byte` - The length of the room name string
 - `byte[]` - A UTF8-encoded string for the room name
### PLAYER_LEFT = 6
This message contains:
 - `int` - The player's userid
 - `byte` - The length of the room name string
 - `byte[]` - A UTF8-encoded string for the room name
### YOU_LEFT = 7
This message contains:
 - `byte` - The length of the room name string
 - `byte[]` - A UTF8-encoded string for the room name
### ROOM_DATA = 8
This message contains:
 - `byte` - The length of the room name string
 - `byte[]` - A UTF8-encoded string for the room name
 - `int` - The number of client data blocks to read
 - For each client data block:
   - `int` - The userid of this client
   - `byte` - The length of the username string
   - `byte[]` - A UTF8-encoded string for the username
