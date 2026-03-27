using Demo.DataService;
using Grpc.Core;

namespace Demo.GrpcService.Services;

// 職責：橋接其他服務，透過 gRPC 呼叫 DataService
public class DataBridgeGrpcService : DataBridgeService.DataBridgeServiceBase
{
    private readonly DataItemService.DataItemServiceClient _dataClient;

    public DataBridgeGrpcService(DataItemService.DataItemServiceClient dataClient)
    {
        _dataClient = dataClient;
    }

    public override async Task<ItemListResponse> GetAllItemsFromDataService(GetAllItemsRequest request, ServerCallContext context)
    {
        var result = await _dataClient.GetAllItemsAsync(new GetAllDataItemsRequest());

        var response = new ItemListResponse();
        response.Items.AddRange(result.Items.Select(item =>
            new ItemResponse { Id = item.Id, Name = item.Name, Description = item.Description }));

        return response;
    }
}
