namespace Viciotek.Infrastructure.Caching;

/// <summary>
/// Small thread-safe LRU used for decoded thumbnails/icons. It bounds image
/// memory while keeping recently visible covers instant when scrolling back.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _order = new();
    private readonly object _gate = new();

    public LruCache(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(capacity);
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = _order.AddFirst(new Entry(key, value));
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var last = _order.Last;
                if (last is null)
                {
                    break;
                }

                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
        }
    }

    private sealed record Entry(TKey Key, TValue Value);
}
