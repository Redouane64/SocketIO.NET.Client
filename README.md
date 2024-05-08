# SocketIO Client for .NET

Socket.IO .NET Client is an experimental project aims to implement Socket.IO protocol using modern .NET platform features.

Unlike other existing clients which uses events and delegates, This implementation's goal is to use .NET Task-based asynchronous programming techniques.

The client implements `Engine.IO` and `Socket.IO` core protocols.

## Tasks
- **Engine.IO Client**

- [x] Http Polling transport
- [x] Basic Websocket transport
- [x] Send/Receiving plain text and Binary packets

- **Socket.IO Client**

- [ ] Connection with namespaces support
- [ ] Send and receive plain text and binary payloads

## Resources

- Engine.IO protocol: https://socket.io/docs/v4/engine-io-protocol
- Socket.IO protocol: https://socket.io/docs/v4/socket-io-protocol
