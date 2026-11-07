// Reenvía un puerto local a otro servicio. Cypress solo acepta preparar datos en una base local.
const net = require('net');
const [localPort, host, port] = process.argv.slice(2);
net.createServer((client) => {
  const upstream = net.connect(Number(port), host);
  client.pipe(upstream).pipe(client);
  const close = () => { client.destroy(); upstream.destroy(); };
  client.on('error', close);
  upstream.on('error', close);
}).listen(Number(localPort), '127.0.0.1');
