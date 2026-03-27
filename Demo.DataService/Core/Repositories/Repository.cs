using Microsoft.EntityFrameworkCore;

namespace Demo.DataService.Core.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly DbContext _db;

    public Repository(DbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<T>> GetAllAsync()
        => await _db.Set<T>().ToListAsync();

    public async Task<T?> GetByIdAsync(int id)
        => await _db.Set<T>().FindAsync(id);

    public async Task AddAsync(T entity)
        => await _db.Set<T>().AddAsync(entity);

    public Task DeleteAsync(T entity)
    {
        _db.Set<T>().Remove(entity);
        return Task.CompletedTask;
    }

    public async Task SaveAsync()
        => await _db.SaveChangesAsync();
}
