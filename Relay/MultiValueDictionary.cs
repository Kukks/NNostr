using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
/// <summary>
/// https://stackoverflow.com/a/60719233/275504
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class ConcurrentMultiDictionary<TKey, TValue>
    : IEnumerable<KeyValuePair<TKey, TValue[]>>
{
    private class Bag : HashSet<TValue>
    {
        public bool IsDiscarded { get; set; }
    }

    private readonly ConcurrentDictionary<TKey, Bag> _dictionary;

    public ConcurrentMultiDictionary()
    {
        _dictionary = new ConcurrentDictionary<TKey, Bag>();
    }

    public int Count => _dictionary.Count;

    public bool Add(TKey key, TValue value)
    {
        var spinWait = new SpinWait();
        while (true)
        {
            var bag = _dictionary.GetOrAdd(key, _ => new Bag());
            lock (bag)
            {
                if (!bag.IsDiscarded) return bag.Add(value);
            }
            spinWait.SpinOnce();
        }
    }

    public bool Remove(TKey key)
    {
        return _dictionary.TryRemove(key, out _);
    }
    public bool Remove(TKey key, TValue value)
    {
        var spinWait = new SpinWait();
        while (true)
        {
            if (!_dictionary.TryGetValue(key, out var bag)) return false;
            bool spinAndRetry = false;
            lock (bag)
            {
                if (bag.IsDiscarded)
                {
                    spinAndRetry = true;
                }
                else
                {
                    bool valueRemoved = bag.Remove(value);
                    if (!valueRemoved) return false;
                    if (bag.Count != 0) return true;
                    bag.IsDiscarded = true;
                }
            }
            if (spinAndRetry) { spinWait.SpinOnce(); continue; }
            bool keyRemoved = _dictionary.TryRemove(key, out var currentBag);
            Debug.Assert(keyRemoved, $"Key {key} was not removed");
            Debug.Assert(bag == currentBag, $"Removed wrong bag");
            return true;
        }
    }

    public bool TryGetValues(TKey key, out TValue[] values)
    {
        if (!_dictionary.TryGetValue(key, out var bag)) { values = null; return false; }
        bool isDiscarded;
        lock (bag) { isDiscarded = bag.IsDiscarded; values = bag.ToArray(); }
        if (isDiscarded) { values = null; return false; }
        return true;
    }

    public bool Contains(TKey key, TValue value)
    {
        if (!_dictionary.TryGetValue(key, out var bag)) return false;
        lock (bag) return !bag.IsDiscarded && bag.Contains(value);
    }

    public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

    public ICollection<TKey> Keys => _dictionary.Keys;

    public IEnumerator<KeyValuePair<TKey, TValue[]>> GetEnumerator()
    {
        foreach (var key in _dictionary.Keys)
        {
            if (this.TryGetValues(key, out var values))
            {
                yield return new KeyValuePair<TKey, TValue[]>(key, values);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool ContainsValue(TValue value)
    {
        return _dictionary.Keys.Any(key => Contains(key, value));
    }
}