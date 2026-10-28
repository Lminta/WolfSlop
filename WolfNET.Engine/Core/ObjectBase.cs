using System.Collections.ObjectModel;

namespace WolfNET.Engine.Core;

public abstract class ObjectBase : IDisposable
{
    private readonly Dictionary<Guid, ComponentBase> _components = [];
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();
    public IReadOnlyDictionary<Guid, ComponentBase> Components { get; }
    public Transform Transform { get; set; }

    protected ObjectBase()
    {
        Components = new ReadOnlyDictionary<Guid, ComponentBase>(_components);
    }

    public virtual void Update(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var component in _components.Values) component.Update(deltaTime);
    }

    public void AddComponent(ComponentBase component)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(component);
        if (!ReferenceEquals(component.Parent, this))
            throw new ArgumentException("A component belongs to its parent object.", nameof(component));
        _components.TryAdd(component.Id, component);
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var component in _components.Values.OfType<IDisposable>()) component.Dispose();
        _components.Clear();
    }
}
