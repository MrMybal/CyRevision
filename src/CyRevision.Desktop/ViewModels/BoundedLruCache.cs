using System.Diagnostics.CodeAnalysis;

namespace CyRevision.Desktop.ViewModels;

/// <summary>
/// Small thread-safe LRU cache with an optional weight budget. It avoids the
/// large all-at-once cache clears that caused pauses while browsing diffs.
/// </summary>
internal sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _maximumEntries;
    private readonly long _maximumWeight;
    private readonly Func<TValue, long> _getWeight;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _items;
    private readonly LinkedList<Entry> _usage = [];
    private readonly object _sync = new();
    private long _currentWeight;

    public BoundedLruCache(
        int maximumEntries,
        long maximumWeight = long.MaxValue,
        Func<TValue, long>? getWeight = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        if (maximumEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumWeight <= 0) throw new ArgumentOutOfRangeException(nameof(maximumWeight));
        _maximumEntries = maximumEntries;
        _maximumWeight = maximumWeight;
        _getWeight = getWeight ?? (_ => 1);
        _items = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_sync)
        {
            if (!_items.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                value = default!;
                return false;
            }
            _usage.Remove(node);
            _usage.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value)
    {
        long weight = Math.Max(0, _getWeight(value));
        lock (_sync)
        {
            if (_items.Remove(key, out LinkedListNode<Entry>? existing))
            {
                _usage.Remove(existing);
                _currentWeight -= existing.Value.Weight;
            }

            if (weight > _maximumWeight) return;
            LinkedListNode<Entry> node = _usage.AddFirst(new Entry(key, value, weight));
            _items[key] = node;
            _currentWeight += weight;
            while (_items.Count > _maximumEntries || _currentWeight > _maximumWeight)
            {
                LinkedListNode<Entry>? oldest = _usage.Last;
                if (oldest is null) break;
                _usage.RemoveLast();
                _items.Remove(oldest.Value.Key);
                _currentWeight -= oldest.Value.Weight;
            }
        }
    }

    private sealed record Entry(TKey Key, TValue Value, long Weight);
}
