namespace WolfSlop.Engine.World;

public interface INavigationMap
{
    bool IsWalkable(int x, int y, bool inAir = false, bool crouching = false);
    string GetSurface(int x, int y) => "mud";
}

// Its identity stays stable while edited map revisions replace the grid.
public sealed class NavigationMap : INavigationMap
{
    private bool[][] _walkable = [];
    private bool[][] _jumpable = [];
    private bool[][] _crouchable = [];
    private string[][] _surfaces = [];

    internal void Replace(bool[][] walkable, bool[][] jumpable, bool[][] crouchable, string[][]? surfaces = null)
    {
        _walkable = walkable;
        _jumpable = jumpable;
        _crouchable = crouchable;
        _surfaces = surfaces ?? [];
    }

    internal void SetWalkable(int x, int y, bool value) => _walkable[x][y] = value;

    public string GetSurface(int x, int y) =>
        x >= 0 && x < _surfaces.Length && y >= 0 && y < _surfaces[x].Length ? _surfaces[x][y] : "mud";

    public bool IsWalkable(int x, int y, bool inAir = false, bool crouching = false) =>
        x >= 0 && x < _walkable.Length && y >= 0 && y < _walkable[x].Length &&
        (_walkable[x][y] || (inAir && _jumpable[x][y]) || (!inAir && crouching && _crouchable[x][y]));
}
