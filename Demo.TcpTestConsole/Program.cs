using Demo.TcpTestConsole;

await using var client = await TcpClientHelper.ConnectAsync("localhost", 5400);

// Category=1 (Item), SubType=1 (Query)
await client.SendAsync(category: 1, subType: 1, data: "Sword");
Console.WriteLine($"[Client] Received: {await client.ReceiveAsync()}\n");

// Category=1 (Item), SubType=2 (Create)
await client.SendAsync(category: 1, subType: 2, data: "Magic Staff");
Console.WriteLine($"[Client] Received: {await client.ReceiveAsync()}\n");

// Category=2 (System), SubType=1 (Heartbeat)
await client.SendAsync(category: 2, subType: 1, data: "");
Console.WriteLine($"[Client] Received: {await client.ReceiveAsync()}\n");

// 未知類型，測試錯誤處理
await client.SendAsync(category: 9, subType: 9, data: "???");
Console.WriteLine($"[Client] Received: {await client.ReceiveAsync()}\n");
