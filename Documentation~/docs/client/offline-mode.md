VelNet supports an offline mode that lets you write client code in the same way even in a situation where there is no internet or no server available. 

Offline mode is activated automatically if no initial TCP connection can be established by `VelNetManager.cs`

Messages are intercepted by VelNetManager and send to a "FakeServer" function instead of to the server, and realistic response messages are constructed and sent to the callback functions.