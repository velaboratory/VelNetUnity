# VelNet Server

[GitHub Link :simple-github:](https://github.com/velaboratory/VelNetServerRust){ .md-button }

The VelNet server acts as a relay between users of your application. It is not aware of the details of your application, and a single server can be used to serve many different applications.

To get started, either use the demo server at `velnet-demo.ugavel.com`, or [host it yourself](self-host.md)

This basic, single-file relay server is designed to be used for network games, and is similar to Photon Realtime in design. It is written in Rust, with a single-threaded, non-blocking design and does not rely on any network frameworks (pure TCP/UDP). A Unity/C# client implementation can be found in our [VelNetUnity](https://github.com/velaboratory/VelNetUnity) repository.

Like Photon, there is no built-in persistence of rooms or data. Rooms are created when the first client joins and destroyed when the last client leaves.

The only game logic implemented by the server is that of a "master client", which is an easier way to negotiate a leader in a room that can perform room level operations.

The "group" functionality is used to specify specific clients to communicate with. Note, these client ids can bridge across rooms.

The server supports both TCP and UDP transports.
