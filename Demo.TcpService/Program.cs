using Demo.TcpService;

var cts = new CancellationTokenSource();

// Ctrl+C 優雅關閉
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new TcpServer(port: 5400);

try
{
    await server.StartAsync(cts.Token);
}
finally
{
    server.Stop();
    Console.WriteLine("[Server] Stopped.");
}
