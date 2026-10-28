namespace WolfNET.Engine.Input;

public interface ISteeringInput
{
    float Steering { get; }
}

public interface IKeyboardInput
{
    KeyState KeyState { get; }
    bool ConsumeJump();
    bool ConsumeViewToggle();
    bool ConsumeAction();
}
