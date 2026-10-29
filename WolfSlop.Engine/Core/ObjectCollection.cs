namespace WolfSlop.Engine.Core;

public sealed class ObjectCollection : IDisposable
{
    private readonly Dictionary<Guid, ObjectBase> _objects = [];
    private bool _disposed;

    public void Update(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var (_, obj) in _objects)
        {
            obj.Update(deltaTime);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var obj in _objects.Values) obj.Dispose();
        _objects.Clear();
    }

    public void Add(ObjectBase obj)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(obj);
        _objects.TryAdd(obj.Id, obj);
    }
}