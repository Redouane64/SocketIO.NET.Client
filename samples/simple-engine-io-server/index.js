const engine = require('engine.io');

const PORT = process.env.PORT || 9854
const httpServer = require('http').createServer().listen(PORT, 
  () => { process.stdout.write(`Server is listening on port ${PORT} ...\n`);
});

const server = engine.attach(httpServer, {
  cors: {
    origin: '*'
  },
});

httpServer.on('request', (req, res) => {
  server.handleRequest(req, res);
});

server.on('connection', socket => {
  socket.on('message', data => { 
    console.log(`[Client] ${data}`);
    socket.send(data);
  });
  
  socket.on('close', () => { 
    console.log(`[Client] disconnected`);
  });
  
  setInterval(() => {
    socket.send("Hello!!!")
  }, 5000)
});