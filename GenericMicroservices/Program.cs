using Microsoft.EntityFrameworkCore;
using GenericMicroservices.Core.Repositories;
using GenericMicroservices.Data;
using GenericMicroservices.Features.Items;

var builder = WebApplication.CreateBuilder(args);

// Register PostgreSQL via EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Repository — DI 會自動把 AppDbContext 注入進去
builder.Services.AddScoped<IRepository<Item>, ItemRepository>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Auto-create tables if they don't exist
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();

// GET all items
app.MapGet("/api/items", async (IRepository<Item> repo) =>
    await repo.GetAllAsync());

// GET single item
app.MapGet("/api/items/{id}", async (int id, IRepository<Item> repo) =>
    await repo.GetByIdAsync(id) is Item item ? Results.Ok(item) : Results.NotFound());

// POST create item
app.MapPost("/api/items", async (Item item, IRepository<Item> repo) =>
{
    await repo.AddAsync(item);
    await repo.SaveAsync();
    return Results.Created($"/api/items/{item.Id}", item);
});

// DELETE item
app.MapDelete("/api/items/{id}", async (int id, IRepository<Item> repo) =>
{
    var item = await repo.GetByIdAsync(id);
    if (item is null) return Results.NotFound();
    await repo.DeleteAsync(item);
    await repo.SaveAsync();
    return Results.NoContent();
});

app.Run();
