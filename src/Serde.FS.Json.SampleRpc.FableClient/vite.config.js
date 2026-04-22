export default {
    server: {
        port: 3000,
        proxy: {
            '/rpc': {
                target: 'http://localhost:5050',
                changeOrigin: true
            }
        }
    }
}
