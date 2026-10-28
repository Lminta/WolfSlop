namespace WolfNET.Engine.Core;

public abstract class ComponentBase(ObjectBase parent)
{
    public Guid Id { get; } = Guid.NewGuid();
    public ObjectBase Parent { get; } = parent ?? throw new ArgumentNullException(nameof(parent));

    /// <param name="deltaTime">Elapsed simulation time in milliseconds.</param>
    public virtual void Update(float deltaTime) { }
}
