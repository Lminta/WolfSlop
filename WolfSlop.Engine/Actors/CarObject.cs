using System.Numerics;
using WolfNET.Engine.Core;
using WolfNET.Engine.Input;
using WolfNET.Engine.Actors.Components;

namespace WolfNET.Engine.Actors;

public sealed class CarObject : ObjectBase, IControlledActor
{
    private readonly IKeyboardInput _input;
    private readonly IMouseInput _mouse;
    public CarSettings Settings { get; }
    public CameraComponent Camera { get; }
    public CarCamera Look { get; }
    public CarController Controller { get; }
    public bool InAir => false;
    public bool Crouching => false;
    public int AnimationFrame => 0;
    public float InteractionRadius => MathF.Sqrt(Settings.Length * Settings.Length + Settings.Width * Settings.Width) * .5f;

    public CarObject(IKeyboardInput input, IMouseInput mouse, NavigationMap map, CarSettings settings,
        CameraShakeSettings? shakeSettings = null)
    {
        _input = input;
        _mouse = mouse;
        Settings = settings;
        Look = new CarCamera(shakeSettings);
        Camera = new CameraComponent(this);
        Controller = new CarController(this, input, map,
            position => map.GetSurface((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y)), settings);
        Controller.WorldImpact += impact => Look.TriggerImpact(impact.Speed);
        AddComponent(Controller);
    }

    public override void Update(float deltaTime)
    {
        if (_input.ConsumeViewToggle()) Camera.ThirdPerson = !Camera.ThirdPerson;
        var movement = _mouse.ConsumeMovement();
        if (Camera.ThirdPerson) Look.Update(deltaTime / 1000, movement, Settings);
        else Look.ResetOrbit(); // Consume and discard cockpit mouse input, including stale orbit offsets.
        base.Update(deltaTime);
        Look.UpdateShake(deltaTime / 1000, Controller.Speed, Controller.MudShakeStrength);
    }
}
