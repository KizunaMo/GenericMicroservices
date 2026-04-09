using Demo.LockstepService;

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// 玩家清單：測試時先寫死兩個 ID，之後可改成動態加入
var playerIds = new List<string> { "PlayerA", "PlayerB" };

var server = new LockstepServer(port: 5500, playerIds);

try
{
    await server.StartAsync(cts.Token);
}
finally
{
    server.Stop();
    Console.WriteLine("[LockstepServer] Stopped.");
}
