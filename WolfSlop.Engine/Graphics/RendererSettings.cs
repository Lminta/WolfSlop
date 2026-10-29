namespace WolfSlop.Engine.Graphics;

public class RendererSettings(int width = 1920, int height = 1080, int targetFPS = 60)
{
    public int Width { get; private set; } = width;
    public int Height { get; private set; } = height;
    public float CameraPlaneScale => MathF.Tan(FovDegrees * MathF.PI / 360f);
    public float AR => Width / (float)Height;
    
    public int TargetFPS { get; private set; } = targetFPS;
    public float TargetFrameInterval => 1000.0f / TargetFPS;

    public ushort MaxAnisotropy { get; set; } = 4;
    // 1 refreshes every column; 2 or 3 rotate through subsets of columns each frame.
    public int ColumnInterlaceFactor { get; set; } = 1;
    // Horizontal field of view. The default preserves the original 0.66 camera plane.
    public float FovDegrees { get; set; } = 66.85f;
    // Horizontal world distances in map squares.
    public float RenderDistance { get; set; } = 60;
    public float FogStartDistance { get; set; } = 40;
    
    public int MaxMapWidth { get; init; } = 512;
    public int MaxMapHeight { get; init; } = 512;

    /// <summary>Applies validated display options while retaining allocated map capacities.</summary>
    public void Apply(RendererSettings settings)
    {
        settings.Validate();
        Width = settings.Width;
        Height = settings.Height;
        TargetFPS = settings.TargetFPS;
        MaxAnisotropy = settings.MaxAnisotropy;
        ColumnInterlaceFactor = settings.ColumnInterlaceFactor;
        FovDegrees = settings.FovDegrees;
        RenderDistance = settings.RenderDistance;
        FogStartDistance = settings.FogStartDistance;
    }

    public void Validate()
    {
        if (MaxAnisotropy is < 1 or > 16)
            throw new ArgumentException("Graphics maxAnisotropy must be between 1 and 16.");
        if (ColumnInterlaceFactor is < 1 or > 3)
            throw new ArgumentException("Graphics columnInterlaceFactor must be 1, 2, or 3.");
        if (TargetFPS <= 0)
            throw new ArgumentException("Graphics targetFPS must be positive.");
        if (!float.IsFinite(FovDegrees) || FovDegrees < 1 || FovDegrees > 179)
            throw new ArgumentException("Graphics fovDegrees must be between 1 and 179 degrees.");
        if (!float.IsFinite(RenderDistance) || RenderDistance <= 0)
            throw new ArgumentException("Graphics renderDistance must be finite and positive.");
        if (!float.IsFinite(FogStartDistance) || FogStartDistance < 0 || FogStartDistance >= RenderDistance)
            throw new ArgumentException("Graphics fogStartDistance must be finite, nonnegative, and less than renderDistance.");

        if (Width <= 0 ||
            Height <= 0)
        {
            throw new ArgumentException(
                "Renderer resolution must be positive.");
        }

        if (MaxMapWidth <= 0 ||
            MaxMapHeight <= 0)
        {
            throw new ArgumentException(
                "Maximum map dimensions must be positive.");
        }

        checked
        {
            _ = MaxMapWidth *
                MaxMapHeight;
        }
    }

}
