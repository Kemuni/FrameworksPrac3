using FrameworksPrac3.Domain;

namespace FrameworksPrac3.Services;

public interface IProductRepository
{
    IReadOnlyCollection<Item> GetAll();

    Item? GetById(Guid id);

    Item Create(string name, decimal price);
}
