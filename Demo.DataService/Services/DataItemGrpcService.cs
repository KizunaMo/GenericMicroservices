using Demo.DataService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Demo.DataService.Services;

public class DataItemGrpcService : DataItemService.DataItemServiceBase
{
    private readonly AppDbContext _db;

    public DataItemGrpcService(AppDbContext db)
    {
        _db = db;
    }

    public override async Task<DataItemResponse> GetItem(GetDataItemRequest request, ServerCallContext context)
    {
        var item = await _db.Items.FindAsync(request.Id);

        if (item is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Item {request.Id} not found"));

        return new DataItemResponse { Id = item.Id, Name = item.Name, Description = item.Description };
    }

    public override async Task<DataItemListResponse> GetAllItems(GetAllDataItemsRequest request, ServerCallContext context)
    {
        var items = await _db.Items.ToListAsync();

        var response = new DataItemListResponse();
        response.Items.AddRange(items.Select(item =>
            new DataItemResponse { Id = item.Id, Name = item.Name, Description = item.Description }));

        return response;
    }
}
