const { Server } = require('socket.io');

const io = new Server({
    cors: {
        origin: '*',
        methods: ['GET', 'POST']
    },
})


io.on("connection", (socket) => {
    // ...
    socket.on('data', (data) => {
        console.log(data);
    })
});

io.listen(process.env.PORT || 3000);
