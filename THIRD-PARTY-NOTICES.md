# Third-party notices

## NativeWebSocket

`Runtime/WebSocket/` contains a modified copy of [NativeWebSocket](https://github.com/endel/NativeWebSocket)
(upm branch, commit ea014c9, version 1.1.6), used for VelNet's WebSocket fallback. Changes: namespace
`NativeWebSocket` → `VelNet.WebSockets`, all types made internal, JavaScript entry points renamed `VelNetWebSocket*`.
`VelNetWebSocket.jspre` was authored by Stephen Hodgson (MIT License).

```
Copyright 2019 Endel Dreyer <endel.dreyer@gmail.com>
Copyright 2018 Jiri Hybek <jiri@hybek.cz>

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

```
