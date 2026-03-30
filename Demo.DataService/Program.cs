using Demo.Contracts;
using MassTransit;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Demo.DataService.Core.Common;
using Demo.DataService.Core.Common.Middleware;
using Demo.DataService.Core.Repositories;
using Demo.DataService.Data;
using Demo.DataService.Features.Items;
using Demo.DataService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5128, o => o.Protocols = HttpProtocols.Http1);   // REST（0.0.0.0，Docker 可連入）
    options.ListenAnyIP(5129, o => o.Protocols = HttpProtocols.Http2);   // gRPC（0.0.0.0，Docker 可連入）
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IRepository<Item>, ItemRepository>();

// MassTransit：連到 RabbitMQ，作為 Publisher
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
    });
});

builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Swagger 只在 Development 開啟，正式環境不暴露 API 文件
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// GET all items
app.MapGet("/api/items", async (IRepository<Item> repo) =>
{
    var items = await repo.GetAllAsync();
    return Results.Ok(ApiResponse<IEnumerable<Item>>.Ok(items));
});

// GET single item
app.MapGet("/api/items/{id}", async (int id, IRepository<Item> repo) =>
{
    var item = await repo.GetByIdAsync(id);
    return item is not null
        ? Results.Ok(ApiResponse<Item>.Ok(item))
        : Results.NotFound(ApiResponse<Item>.Fail($"Item with id {id} not found"));
});

// POST create item → 儲存後發布 Domain Event
app.MapPost("/api/items", async (Item item, IRepository<Item> repo, IPublishEndpoint publisher) =>
{
    await repo.AddAsync(item);
    await repo.SaveAsync();

    // 發布 Domain Event（非同步，不等待 Consumer 處理完）
    await publisher.Publish(new ItemCreated
    {
        Id = item.Id,
        Name = item.Name,
        CreatedAt = DateTime.UtcNow
    });

    return Results.Created($"/api/items/{item.Id}", ApiResponse<Item>.Ok(item));
});

// DELETE item
app.MapDelete("/api/items/{id}", async (int id, IRepository<Item> repo) =>
{
    var item = await repo.GetByIdAsync(id);
    if (item is null)
        return Results.NotFound(ApiResponse<Item>.Fail($"Item with id {id} not found"));

    await repo.DeleteAsync(item);
    await repo.SaveAsync();
    return Results.NoContent();
});

app.MapGrpcService<DataItemGrpcService>();
app.MapHealthChecks("/health");   // Docker 用來探測服務是否 ready

app.Run();
