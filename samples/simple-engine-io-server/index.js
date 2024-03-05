const engine = require('engine.io');

const PORT = process.env.PORT || 9854
const httpServer = require('http').createServer().listen(PORT, 
  () => { process.stdout.write(`Server is listening on port ${PORT} ...\n`);
});

const server = engine.attach(httpServer, {
  cors: {
    origin: '*'
  },
  pingInterval: 10000,
  pingTimeout: 20000,
  transports: ['polling', "websocket"],
  allowUpgrades: true,
});

httpServer.on('request', (req, res) => {
  server.handleRequest(req, res);
});

server.on('connection_error', (error) => {
  process.stderr.write(`Error:\n${JSON.stringify(error)}\n\n`)
})

httpServer.on('upgrade', (req, socket, head) => {
  server.handleUpgrade(req, socket, head);
})

server.on('connection', socket => {
  console.log('[Client] connected')
  socket.on('message', data => { 
    console.log(`[Client] ${data}`);
    socket.send(data);
  });
  
  socket.on('close', () => { 
    console.log(`[Client] disconnected`);
  });
  
  /*
  setInterval(() => {
    socket.send("Hello!!!");
  }, 10000)

  setTimeout(() => {
    socket.close();
  }, 15000)
  */
});