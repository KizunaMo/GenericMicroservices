using Demo.DataService.Core.Repositories;
using Demo.DataService.Data;

namespace Demo.DataService.Features.Items;

public class ItemRepository : Repository<Item>
{
    public ItemRepository(AppDbContext db) : base(db)
    {
    }
}
