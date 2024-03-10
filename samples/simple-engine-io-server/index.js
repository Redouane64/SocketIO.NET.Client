const engine = require('engine.io');

const PORT = process.env.PORT || 9854
const httpServer = require('http').createServer().listen(PORT, 
  () => { process.stdout.write(`Server is listening on port ${PORT} ...\n`);
});

const server = engine.attach(httpServer, {
  cors: {
    origin: '*'
  },
  pingInterval: 300,
  pingTimeout: 200,
  transports: ['polling', "websocket"],
  allowUpgrades: true,
});

httpServer.on('request', (req, res) => {
  server.handleRequest(req, res);
});

httpServer.on('upgrade', (req, socket, head) => {
  server.handleUpgrade(req, socket, head);
})

server.on('connection', socket => {
  console.log('[Client] connected')
  
  socket.on('close', () => { 
    console.log(`[Client] disconnected`);
  });
  
  socket.on('message', data => { 
    console.log(`[Client] ${data}`);
    socket.send(data);
  });
  
  setInterval(() => {
    socket.send("Hello!!!");
  }, 5000)

  /*
  setTimeout(() => {
    socket.close();
  }, 15000)
  */
});