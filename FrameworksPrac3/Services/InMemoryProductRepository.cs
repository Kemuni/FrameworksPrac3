using System.Collections.Concurrent;
using FrameworksPrac3.Domain;

namespace FrameworksPrac3.Services;

public sealed class InMemoryProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<Guid, Item> _items = new();

    public IReadOnlyCollection<Item> GetAll()
        => _items.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public Item? GetById(Guid id)
        => _items.TryGetValue(id, out var item) ? item : null;

    public int GetCount() => _items.Count;

    public bool DeleteById(Guid id)
    {
        return _items.TryRemove(id, out _);
    }

    public Item Create(string name, decimal price)
    {
        var id = Guid.NewGuid();
        var item = new Item(id, name, price);
        _items[id] = item;
        return item;
    }
}
