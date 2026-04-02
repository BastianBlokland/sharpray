using System;

class Mesh : IShape
{
    private Triangle[] _triangles;
    private Bvh<Triangle> _bvh;
    private Counters? _counters;

    public Mesh(Triangle[] triangles, Counters? counters = null)
    {
        _triangles = triangles;
        using (counters?.TimeScope(Counters.Type.TimeMeshBvhBuild))
        {
            _bvh = new Bvh<Triangle>(_triangles, sahCostIntersect: 0.5f); // sahCostIntersect low as the triangle test is cheap.
        }
        _counters = counters;

        counters?.Bump(Counters.Type.MeshTriangle, triangles.Length);
    }

    public AABox Bounds() => _bvh.Bounds;
    public bool Overlaps(AABox box) => _bvh.Overlaps(box, _counters);

    public RayHit? Intersect(Ray ray)
    {
        _counters?.Bump(Counters.Type.MeshIntersect);
        return _bvh.Intersect(ray, _counters)?.Hit;
    }

    public bool IntersectAny(Ray ray)
    {
        _counters?.Bump(Counters.Type.MeshIntersectAny);
        return _bvh.IntersectAny(ray, _counters);
    }

    public void OverlayBounds(Overlay overlay, Transform trans, int maxDepth = int.MaxValue) =>
        _bvh.OverlayBounds(overlay, trans, maxDepth);

    public void OverlayWireframe(Overlay overlay, Transform trans, Color color)
    {
        foreach (Triangle tri in _triangles)
        {
            Vec3 a = trans.TransformPoint(tri.PosA);
            Vec3 b = trans.TransformPoint(tri.PosB);
            Vec3 c = trans.TransformPoint(tri.PosC);

            overlay.AddLine(new Line(a, b), color);
            overlay.AddLine(new Line(b, c), color);
            overlay.AddLine(new Line(c, a), color);
        }
    }

    public void Describe(ref FormatWriter fmt)
    {
        BvhStats bvhStats = _bvh.GetStats();
        fmt.WriteLine($"tris={new FormatNum(_triangles.Length)}");
        fmt.WriteLine($"nodes={new FormatNum(bvhStats.NodeCount)}");
        fmt.WriteLine($"depth={bvhStats.DepthMax}");
        fmt.WriteLine($"leafCount={new FormatNum(bvhStats.LeafCount)}");
        fmt.WriteLine($"leafSize=({bvhStats.LeafSizeMin}/{bvhStats.LeafSizeAvg:F1}/{bvhStats.LeafSizeMax})");
        fmt.WriteLine($"leafDepth={bvhStats.LeafDepthAvg:F1}");
        fmt.WriteLine($"sah={bvhStats.SahCost:F1}"); // Surface area heuristic, how many things to test (boxes and shapes) for a random ray.
    }
}
