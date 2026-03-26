using GenericMicroservices.Features.Items;
using Microsoft.EntityFrameworkCore;

namespace GenericMicroservices.Data;

// DbContext is the bridge between your C# code and the database.
// Each DbSet<T> maps to one table in PostgreSQL.
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items { get; set; }
}