using Grpc.Net.Client;
using Demo.GrpcService;

using var channel = GrpcChannel.ForAddress("http://localhost:5300");
var client = new ItemService.ItemServiceClient(channel);

// 取得所有 Items
Console.WriteLine("=== GetAllItems ===");
var allItems = await client.GetAllItemsAsync(new GetAllItemsRequest());
foreach (var item in allItems.Items)
{
    Console.WriteLine($"[{item.Id}] {item.Name} - {item.Description}");
}

// 取得單筆 Item
Console.WriteLine("\n=== GetItem (id=2) ===");
var single = await client.GetItemAsync(new GetItemRequest { Id = 2 });
Console.WriteLine($"[{single.Id}] {single.Name} - {single.Description}");

// 測試 Not Found
Console.WriteLine("\n=== GetItem (id=99) ===");
try
{
    var notFound = await client.GetItemAsync(new GetItemRequest { Id = 99 });
}
catch (Grpc.Core.RpcException ex)
{
    Console.WriteLine($"gRPC Error: {ex.StatusCode} - {ex.Status.Detail}");
}
