There are several steps that need to happen before a client can join a room. Most of these can happen automatically using the default `VelNetManager` script.

## 1. Connecting
First, the TCP connection to the server must be established. If this step fails, offline mode is activated.

## 2. Logging in
Logging in registers this client with the server. The inputs for this are an app name and device id. `VelNetManager` automatically provides an appName that is appended with the version number if `onlyConnectToSameVersion` is true. The device id is generated from Unity's `SystemInfo.deviceUniqueIdentifier` and the app name to guarantee a different id per app and device.

## 3. Joining a room
Only after joining a room can normal networking take place between clients in that room. The client can send any string room name to join, and other clients that send the same string will be in that room with them.