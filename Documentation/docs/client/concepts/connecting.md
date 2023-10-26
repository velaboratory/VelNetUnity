There are several steps that need to happen before a client can join a room. Most of these can happen automatically using the default `VelNetManager` script.

## 1. Connecting
First, the TCP connection to the server must be established. If this step fails, offline mode is activated. This step happens automatically on Play in `VelNetManager`.

## 2. Logging in
```cs
void Login(string appName, string deviceId)
```
Logging in registers this client with the server. The inputs for this are an app name and device id. By default, `VelNetManager` automatically logs in after a connection has been established.

If `onlyConnectToSameVersion` is true, `VelNetManager` calls Login with:
 - appName: `$"{Application.productName}_{Application.version}"`
 - deviceId: `Hash128.Compute(SystemInfo.deviceUniqueIdentifier + Application.productName).ToString()`
   - This guarantees a different deviceId per application per device

otherwise it uses just `Application.productName` for the appName field.

## 3. Joining a [room](/client/concepts/rooms)
```cs
void JoinRoom(string roomName)
```
Only after joining a room can normal networking take place between clients in that room. The client can send any string room name to join, and other clients that send the same string will be in that room with them. To leave a room, either use the `VelNetManager.LeaveRoom()` method, or call `JoinRoom("")` with an empty string for the room name.