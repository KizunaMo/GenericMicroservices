using Microsoft.EntityFrameworkCore;
using GenericMicroservices.Data;
using GenericMicroservices.Models;

var builder = WebApplication.CreateBuilder(args);

// Register PostgreSQL via EF Core
// AppDbContext is injected anywhere it's needed via DI
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
app.MapGet("/api/items", async (AppDbContext db) =>
    await db.Items.ToListAsync());

// GET single item
app.MapGet("/api/items/{id}", async (int id, AppDbContext db) =>
    await db.Items.FindAsync(id) is Item item ? Results.Ok(item) : Results.NotFound());

// POST create item
app.MapPost("/api/items", async (Item item, AppDbContext db) =>
{
    db.Items.Add(item);
    await db.SaveChangesAsync();
    return Results.Created($"/api/items/{item.Id}", item);
});

// DELETE item
app.MapDelete("/api/items/{id}", async (int id, AppDbContext db) =>
{
    var item = await db.Items.FindAsync(id);
    if (item is null) return Results.NotFound();
    db.Items.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();