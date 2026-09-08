using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EngineIO.Client.Tests")]

// The Socket.IO layer needs the same stubbed-transport seam its tests do.
[assembly: InternalsVisibleTo("SocketIO.Client")]