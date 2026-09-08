const { Server } = require('socket.io');

const io = new Server({
    cors: {
        origin: '*',
        methods: ['GET', 'POST']
    },
});

io.on('connection', (socket) => {
    socket.on('message', (data, callback) => {
        console.log(data);
        callback?.apply(this, ['OK']);
    });
});

io.listen(Number(process.env.PORT) || 9855);
