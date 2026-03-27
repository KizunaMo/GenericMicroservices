using Demo.GrpcService.Data;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using GenericMicroservices;

namespace Demo.GrpcService.Services;

public class ItemGrpcService : ItemService.ItemServiceBase
{
    private readonly AppDbContext _db;

    public ItemGrpcService(AppDbContext db)
    {
        _db = db;
    }

    public override async Task<ItemResponse> GetItem(GetItemRequest request, ServerCallContext context)
    {
        var item = await _db.GrpcItems.FindAsync(request.Id);

        if (item is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Item {request.Id} not found"));

        return new ItemResponse { Id = item.Id, Name = item.Name, Description = item.Description };
    }

    public override async Task<ItemListResponse> GetAllItems(GetAllItemsRequest request, ServerCallContext context)
    {
        var items = await _db.GrpcItems.ToListAsync();

        var response = new ItemListResponse();
        response.Items.AddRange(items.Select(item =>
            new ItemResponse { Id = item.Id, Name = item.Name, Description = item.Description }));

        return response;
    }

    public override async Task<ItemListResponse> GetAllItemsFromDataService(GetAllItemsRequest request, ServerCallContext context)
    {
        // 建立連線到 DataService（另一個微服務）
        using var channel = GrpcChannel.ForAddress("http://localhost:5129");
        var client = new DataItemService.DataItemServiceClient(channel);

        // 呼叫 DataService 的 gRPC 方法
        var result = await client.GetAllItemsAsync(new GetAllDataItemsRequest());

        // 將 DataService 的回應格式轉換成自己的格式
        var response = new ItemListResponse();
        response.Items.AddRange(result.Items.Select(item =>
            new ItemResponse { Id = item.Id, Name = item.Name, Description = item.Description }));

        return response;
    }
}
