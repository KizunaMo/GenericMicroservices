using GenericMicroservices.Core.Repositories;
using GenericMicroservices.Data;

namespace GenericMicroservices.Features.Items;

public class ItemRepository : Repository<Item>
{
    public ItemRepository(AppDbContext db) : base(db)
    {
    }
}
