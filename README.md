# SocketIO Client for .NET

Socket.IO .NET Client is an experimental project aims to implement Socket.IO protocol using modern .NET platform features.

Unlike other existing clients which uses events and delegates, This implementation's goal is to use .NET Task-based asynchronous programming techniques.

The client implements `Engine.IO` and `Socket.IO` core protocols.

## TODOs
- **Engine.IO Client**

- [x] Http Polling transport
- [x] Basic Websocket transport
- [x] Send and receiving plain text and Binary packets

- **Socket.IO Client**

- [x] Packet model and wire-format serialization (events, acks, binary attachments)
- [x] Send plain text, JSON and binary payloads
- [ ] Packet parsing (receiving)
- [ ] Namespaces support
- [ ] Acknowledgement correlation

## Resources

- Engine.IO protocol: https://socket.io/docs/v4/engine-io-protocol
- Socket.IO protocol: https://socket.io/docs/v4/socket-io-protocol
