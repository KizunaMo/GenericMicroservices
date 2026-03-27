using Demo.GrpcService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Demo.GrpcService.Services;

// 職責：操作 grpc_db 的 GrpcItems 資料
public class GrpcItemService : ItemService.ItemServiceBase
{
    private readonly AppDbContext _db;

    public GrpcItemService(AppDbContext db)
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
}
