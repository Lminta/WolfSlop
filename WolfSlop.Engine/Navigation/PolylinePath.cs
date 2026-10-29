using System.Numerics;

namespace WolfSlop.Engine.Navigation;

/// <summary>Indexed distance and nearest-point queries along an open or looping polyline.</summary>
public sealed class PolylinePath
{
    private readonly Vector2[] _points;
    private readonly float[] _distance;
    private readonly int[] _segments;
    private readonly Node[] _nodes;
    private int _nodeCount;
    private readonly record struct Node(Vector2 Min, Vector2 Max, int Start, int Count,
        int FirstSegment, int LastSegment, int Left = -1, int Right = -1);
    private struct Nearest
    {
        public float Squared, Distance;
        public int Rank;
    }
    public float Length => _distance[^1];

    private readonly bool _loop;
    private readonly int _searchWindow;
    private readonly float _localSearchSquared;

    public PolylinePath(IEnumerable<Vector2> points, bool loop = false, int searchWindow = 81, float localSearchDistance = 6)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (searchWindow < 1 || !float.IsFinite(localSearchDistance) || localSearchDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(searchWindow));
        _loop = loop; _searchWindow = searchWindow; _localSearchSquared = localSearchDistance * localSearchDistance;
        _points = points.ToArray();
        if (_points.Length < 2 || _points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
            throw new ArgumentException("A path needs at least two finite points.", nameof(points));
        if (loop && _points[0] != _points[^1]) _points = _points.Append(_points[0]).ToArray();
        _distance = new float[_points.Length];
        for (var i = 1; i < _points.Length; i++) _distance[i] = _distance[i - 1] + Vector2.Distance(_points[i - 1], _points[i]);
        if (!float.IsFinite(Length) || Length <= 0) throw new InvalidDataException("A path needs positive finite length.");
        _segments = Enumerable.Range(0, _points.Length - 1).ToArray();
        _nodes = new Node[_segments.Length * 2];
        Build(0, _segments.Length);
    }

    // Immutable segment bounds let off-path queries find the exact nearest
    // segment without repeatedly scanning the whole path in the simulation.
    private int Build(int start, int count)
    {
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        var first = int.MaxValue;
        var last = 0;
        for (var i = start; i < start + count; i++)
        {
            var segment = _segments[i];
            min = Vector2.Min(min, Vector2.Min(_points[segment], _points[segment + 1]));
            max = Vector2.Max(max, Vector2.Max(_points[segment], _points[segment + 1]));
            first = Math.Min(first, segment);
            last = Math.Max(last, segment);
        }
        var index = _nodeCount++;
        _nodes[index] = new(min, max, start, count, first, last);
        if (count <= 8) return index;
        var horizontal = max.X - min.X >= max.Y - min.Y;
        Array.Sort(_segments, start, count, Comparer<int>.Create((a, b) =>
        {
            var centerA = _points[a] + _points[a + 1];
            var centerB = _points[b] + _points[b + 1];
            return (horizontal ? centerA.X : centerA.Y).CompareTo(horizontal ? centerB.X : centerB.Y);
        }));
        var left = Build(start, count / 2);
        var right = Build(start + count / 2, count - count / 2);
        _nodes[index] = _nodes[index] with { Left = left, Right = right };
        return index;
    }

    private float BoundsDistance(int index, Vector2 position)
    {
        ref readonly var node = ref _nodes[index];
        return Vector2.DistanceSquared(position, Vector2.Clamp(position, node.Min, node.Max));
    }

    private void Search(int index, Vector2 position, int start, int count, ref Nearest best)
    {
        ref readonly var node = ref _nodes[index];
        var end = start + count;
        var total = _segments.Length;
        if (end <= total
            ? node.LastSegment < start || node.FirstSegment >= end
            : node.LastSegment < start && node.FirstSegment >= end - total) return;
        if (node.Left >= 0)
        {
            var left = node.Left;
            var right = node.Right;
            var leftDistance = BoundsDistance(left, position);
            var rightDistance = BoundsDistance(right, position);
            if (rightDistance < leftDistance)
            {
                (left, right) = (right, left);
                (leftDistance, rightDistance) = (rightDistance, leftDistance);
            }
            if (leftDistance <= best.Squared) Search(left, position, start, count, ref best);
            if (rightDistance <= best.Squared) Search(right, position, start, count, ref best);
            return;
        }
        for (var step = node.Start; step < node.Start + node.Count; step++)
        {
            var segment = _segments[step];
            var rank = (segment - start + total) % total;
            if (rank >= count) continue;
            var edge = _points[segment + 1] - _points[segment];
            var squaredLength = edge.LengthSquared();
            if (squaredLength == 0) continue;
            var amount = Math.Clamp(Vector2.Dot(position - _points[segment], edge) / squaredLength, 0, 1);
            var squared = Vector2.DistanceSquared(_points[segment] + edge * amount, position);
            if (squared > best.Squared || squared == best.Squared && rank >= best.Rank) continue;
            best.Squared = squared;
            best.Rank = rank;
            best.Distance = _distance[segment] + edge.Length() * amount;
        }
    }

    public Vector2 Point(float distance)
    {
        if (!float.IsFinite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        distance = _loop ? (distance % Length + Length) % Length : Math.Clamp(distance, 0, Length);
        var i = Array.BinarySearch(_distance, distance);
        if (i >= 0) return _points[i];
        i = Math.Clamp(~i, 1, _points.Length - 1);
        return Vector2.Lerp(_points[i - 1], _points[i], (distance - _distance[i - 1]) / (_distance[i] - _distance[i - 1]));
    }

    public float Project(Vector2 position, float? near = null)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || near is { } hint && !float.IsFinite(hint))
            throw new ArgumentOutOfRangeException(nameof(position));
        var best = new Nearest { Squared = float.PositiveInfinity, Rank = int.MaxValue };
        if (near is { } n)
        {
            var normalized = _loop ? (n % Length + Length) % Length : Math.Clamp(n, 0, Length);
            var center = Array.BinarySearch(_distance, normalized);
            if (center < 0) center = ~center;
            var count = Math.Min(_searchWindow, _segments.Length);
            var start = _loop ? (center - count / 2 - 1 + _segments.Length) % _segments.Length
                : Math.Clamp(center - count / 2 - 1, 0, _segments.Length - count);
            Search(0, position, start, count, ref best);
            if (best.Squared > _localSearchSquared)
            {
                // Keep the local winner on an exact tie, as the original scan did.
                best.Rank = -1;
                Search(0, position, 0, _segments.Length, ref best);
            }
        }
        else Search(0, position, 0, _segments.Length, ref best);
        var result = best.Distance;
        if (_loop && near is { } previous) result += MathF.Round((previous - result) / Length) * Length;
        return result;
    }
}
