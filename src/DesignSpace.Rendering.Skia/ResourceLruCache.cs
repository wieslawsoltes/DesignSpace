namespace DesignSpace.Rendering.Skia;

/// <summary>UI-thread cache with both an entry limit and a weighted memory budget.</summary>
internal sealed class ResourceLruCache<TKey,TValue>(int capacity,long budget,Action<TValue>? dispose=null) where TKey:notnull
{
    private sealed record Entry(TKey Key,TValue Value,long Cost);
    private readonly Dictionary<TKey,LinkedListNode<Entry>> _index=[];
    private readonly LinkedList<Entry> _order=[];
    public long Cost { get; private set; }
    public int Count=>_index.Count;
    public bool TryGetValue(TKey key,out TValue value)
    {
        if(!_index.TryGetValue(key,out var node)) { value=default!;return false; }
        _order.Remove(node);_order.AddLast(node);value=node.Value.Value;return true;
    }
    public void Add(TKey key,TValue value,long cost)
    {
        if(cost<0 || cost>budget) throw new ArgumentOutOfRangeException(nameof(cost));
        if(_index.TryGetValue(key,out var old)) Remove(old);
        while(_order.First is { } first && (_index.Count>=capacity || Cost+cost>budget)) Remove(first);
        var node=_order.AddLast(new Entry(key,value,cost));_index.Add(key,node);Cost+=cost;
    }
    private void Remove(LinkedListNode<Entry> node)
    {
        _order.Remove(node);_index.Remove(node.Value.Key);Cost-=node.Value.Cost;dispose?.Invoke(node.Value.Value);
    }
    public void Clear() { while(_order.First is { } first) Remove(first); }
}
