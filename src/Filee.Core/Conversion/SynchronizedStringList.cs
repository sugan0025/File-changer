using System.Collections;

namespace Filee.Core.Conversion;

/// <summary>
/// Thread-safe string list for FileResult.Outputs that provides atomic snapshot enumeration
/// to prevent "Collection was modified" exceptions across concurrent conversion worker and UI threads.
/// </summary>
public sealed class SynchronizedStringList : IList<string>, IReadOnlyList<string>
{
    private readonly List<string> _list = [];
    private readonly Lock _lock = new();

    public int Count { get { lock (_lock) return _list.Count; } }
    public bool IsReadOnly => false;

    public string this[int index]
    {
        get { lock (_lock) return _list[index]; }
        set { lock (_lock) _list[index] = value; }
    }

    public void Add(string item)
    {
        lock (_lock) _list.Add(item);
    }

    public void AddRange(IEnumerable<string> items)
    {
        lock (_lock) _list.AddRange(items);
    }

    public void Clear()
    {
        lock (_lock) _list.Clear();
    }

    public bool Contains(string item)
    {
        lock (_lock) return _list.Contains(item);
    }

    public void CopyTo(string[] array, int arrayIndex)
    {
        lock (_lock) _list.CopyTo(array, arrayIndex);
    }

    public int IndexOf(string item)
    {
        lock (_lock) return _list.IndexOf(item);
    }

    public void Insert(int index, string item)
    {
        lock (_lock) _list.Insert(index, item);
    }

    public bool Remove(string item)
    {
        lock (_lock) return _list.Remove(item);
    }

    public void RemoveAt(int index)
    {
        lock (_lock) _list.RemoveAt(index);
    }

    public IEnumerator<string> GetEnumerator()
    {
        string[] snapshot;
        lock (_lock) snapshot = _list.ToArray();
        return ((IEnumerable<string>)snapshot).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
