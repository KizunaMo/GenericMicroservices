using Microsoft.EntityFrameworkCore;

namespace Demo.AuthService.Data;

public class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<User>         Users         => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
}
