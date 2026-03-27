using Microsoft.EntityFrameworkCore;

namespace Demo.GrpcService.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<GrpcItem> GrpcItems => Set<GrpcItem>();
}
