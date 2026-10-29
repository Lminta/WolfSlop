using System.Numerics;
using WolfSlop.Engine.Core;
using WolfSlop.Engine.Physics;
using WolfSlop.Engine.Input;

namespace WolfSlop.Engine.Actors.Components;

public class WalkingController : ComponentBase
{
    // World units: a cell is one unit wide.
    private float CollisionRadius => _settings.CollisionRadius;
    private readonly IKeyboardInput _keyTracker;
    private readonly IMouseInput _mouseTracker;
    // Radians per pixel before applying the map's maximum turn rate.
    private const float MouseSensitivity = 0.0025f;
    public const float MaxPitch = MathF.PI / 6;
    private readonly CameraComponent _camera;
    private readonly INavigationMap _map;
    private readonly CircleCollider _collision;
    private readonly ActorStartDefinition _settings;
    private float _jumpElapsed;
    private float _jumpTime;
    private float _jumpSpeed;
    private Vector2 _jumpOrigin;
    private Vector2 _jumpDirection;
    private readonly HashSet<(int X, int Y)> _landingBlockers = [];
    public bool InAir { get; private set; }
    public bool Crouching { get; private set; }
    public Vector2 JumpDestination { get; private set; }
    
    public WalkingController(IKeyboardInput keyTracker, IMouseInput mouseTracker, INavigationMap map, CameraComponent camera, ActorStartDefinition settings, ObjectBase parent) : base(parent)
    {
        _keyTracker = keyTracker;
        _mouseTracker = mouseTracker;
        _map = map;
        _collision = new CircleCollider(map, settings.CollisionRadius);
        _camera = camera;
        _settings = settings;
    }

    public override void Update(float deltaTime)
    {
        deltaTime /= 1000f;
        var moveSpeed = deltaTime * _settings.MoveSpeed;
        var rotSpeed = deltaTime * _settings.RotSpeed;

        var posX = Parent.Transform.Position.X;
        var posY = Parent.Transform.Position.Y;
        var dirX = Parent.Transform.Rotation.X;
        var dirY = Parent.Transform.Rotation.Y;

        if (_keyTracker.ConsumeViewToggle()) _camera.ThirdPerson = !_camera.ThirdPerson;
        var keys = _keyTracker.KeyState;
        // Third-person input steers the character first; translation and jump
        // planning must use the same heading that this frame's camera follows.
        if (_camera.ThirdPerson) ApplyLook(rotSpeed, ref dirX, ref dirY);
        // Stay low under an obstacle after Alt is released; stand only when the
        // complete collision circle has cleared it. Crouching never bypasses walls in flight.
        Crouching = !InAir && (keys.Crouch || (Crouching && !CanOccupy(posX, posY, inAir: false, crouching: false)));
        if (Crouching) moveSpeed *= _settings.CrouchSpeedMultiplier;
        var forward = (keys.Forward ? 1 : 0) - (keys.Backward ? 1 : 0);
        var strafe = (keys.StrafeRight ? 1 : 0) - (keys.StrafeLeft ? 1 : 0);
        var movement = new Vector2(dirX, dirY) * forward +
                       new Vector2(dirY, -dirX) * strafe;

        var jumpRequested = _keyTracker.ConsumeJump();
        if (jumpRequested && !InAir && !Crouching && _settings.JumpLength > 0 && _settings.MoveSpeed > 0)
        {
            _jumpOrigin = _collision.ResolveContacts(new(posX, posY), Crouching);
            _jumpDirection = Vector2.Normalize(movement.LengthSquared() > 0 ? movement : new(dirX, dirY));
            _jumpSpeed = _settings.MoveSpeed * _settings.JumpSpeedMultiplier;
            _jumpTime = _settings.JumpLength / _jumpSpeed;
            JumpDestination = PlanJump(_jumpOrigin, _jumpOrigin + _jumpDirection * _settings.JumpLength);
            _jumpElapsed = 0;
            InAir = true;
        }

        // Combine inputs before normalizing so diagonal movement has the same speed.
        if (InAir)
        {
            // Horizontal input and subsequent camera turns cannot steer this trajectory.
            _jumpElapsed = MathF.Min(_jumpTime, _jumpElapsed + deltaTime);
            var distance = MathF.Min(_jumpElapsed * _jumpSpeed, Vector2.Distance(_jumpOrigin, JumpDestination));
            var desired = _jumpOrigin + _jumpDirection * distance;
            // Doors can close after takeoff. Sweep this frame's motion against the
            // current navigation grid instead of trusting only the launch-time plan.
            var position = TraceJump(new(posX, posY), desired);
            if (Vector2.DistanceSquared(position, desired) > .0000000001f) JumpDestination = position;
            posX = position.X;
            posY = position.Y;
            var progress = _jumpElapsed / _jumpTime;
            _camera.Elevation = 4 * _settings.JumpRise * progress * (1 - progress);
            if (progress >= 0.999999f)
            {
                posX = JumpDestination.X;
                posY = JumpDestination.Y;
                InAir = false;
                _camera.Elevation = 0;
                _landingBlockers.Clear();
            }
        }
        else if (movement.LengthSquared() > 0)
        {
            movement = Vector2.Normalize(movement) * moveSpeed;
            var position = _collision.MoveAndSlide(new(posX, posY), movement, Crouching);
            posX = position.X;
            posY = position.Y;
        }
        if (!InAir)
        {
            var target = Crouching ? -_settings.CrouchDrop : 0;
            var change = deltaTime * _settings.CrouchDrop / 0.12f;
            _camera.Elevation += Math.Clamp(target - _camera.Elevation, -change, change);
        }
        if (!_camera.ThirdPerson) ApplyLook(rotSpeed, ref dirX, ref dirY);
        Parent.Transform = new Transform()
        {
            Position = new Vector2(posX, posY),
            Rotation = new Vector2(dirX, dirY)
        };
    }

    private void ApplyLook(float rotSpeed, ref float dirX, ref float dirY)
    {
        var planeX = _camera.Plane.X;
        var planeY = _camera.Plane.Y;
        var mouseMovement = _mouseTracker.ConsumeMovement();
        var turn = -mouseMovement.X * MouseSensitivity;
        var pitchChange = Math.Clamp(-mouseMovement.Y * MouseSensitivity, -rotSpeed, rotSpeed);
        _camera.Pitch = Math.Clamp(_camera.Pitch + pitchChange, -MaxPitch, MaxPitch);
        if (_keyTracker.KeyState.Right)
            turn -= rotSpeed;
        if (_keyTracker.KeyState.Left)
            turn += rotSpeed;
        turn = Math.Clamp(turn, -rotSpeed, rotSpeed);

        if (turn != 0)
        {
            var cos = MathF.Cos(turn);
            var sin = MathF.Sin(turn);
            // Rotate direction and camera plane together to preserve the field of view.
            var oldDirX = dirX;
            dirX = dirX * cos - dirY * sin;
            dirY = oldDirX * sin + dirY * cos;
            var oldPlaneX = planeX;
            planeX = planeX * cos - planeY * sin;
            planeY = oldPlaneX * sin + planeY * cos;
        }

        _camera.Plane = new Vector2(planeX, planeY);
    }

    private Vector2 PlanJump(Vector2 origin, Vector2 desired)
    {
        _landingBlockers.Clear();
        var target = desired;
        // A shortened path may end over another jumpable cell. Promote that cell
        // to a solid obstacle too, until the complete circle has a clear landing.
        for (int attempt = 0; attempt < 4096; attempt++)
        {
            for (int x = (int)MathF.Floor(target.X - CollisionRadius); x <= (int)MathF.Floor(target.X + CollisionRadius); x++)
            for (int y = (int)MathF.Floor(target.Y - CollisionRadius); y <= (int)MathF.Floor(target.Y + CollisionRadius); y++)
            {
                if (_map.IsWalkable(x, y)) continue;
                if (CircleGeometry.OverlapsCell(target, CollisionRadius, x, y))
                    _landingBlockers.Add((x, y));
            }
            target = TraceJump(origin, desired);
            if (CanOccupy(target.X, target.Y, inAir: false)) return target;
        }
        return origin;
    }

    private Vector2 TraceJump(Vector2 origin, Vector2 desired) =>
        _collision.Trace(origin, desired, inAir: true, crouching: Crouching, blockers: _landingBlockers);

    private bool CanOccupy(float x, float y, bool? inAir = null, bool? crouching = null) =>
        _collision.CanOccupy(new(x, y), inAir ?? InAir, crouching ?? Crouching,
            (inAir ?? InAir) ? _landingBlockers : null);
}
