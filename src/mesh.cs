using System;

class Mesh : IShape
{
    private Triangle[] _triangles;
    private TriangleLean[] _trianglesLean;
    private Bvh<TriangleLean, ShapeHitLean> _bvh;
    private Counters? _counters;

    public Mesh(Triangle[] triangles, Counters? counters = null)
    {
        _triangles = triangles;
        _trianglesLean = new TriangleLean[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            _trianglesLean[i] = triangles[i].Lean;
        }
        using (counters?.TimeScope(Counters.Type.TimeMeshBvhBuild))
        {
            const float sahCostIntersect = 0.5f; // sahCostIntersect low as the triangle test is cheap.
            _bvh = new Bvh<TriangleLean, ShapeHitLean>(_trianglesLean, sahCostIntersect: sahCostIntersect);
        }
        _counters = counters;

        counters?.Bump(Counters.Type.MeshTriangle, triangles.Length);
    }

    public AABox Bounds() => _bvh.Bounds;
    public bool Overlaps(AABox box) => _bvh.Overlaps(box, _counters);

    public ShapeHit? Intersect(Ray ray)
    {
        _counters?.Bump(Counters.Type.MeshIntersect);
        if (_bvh.Intersect(ray, _counters) is not (ShapeHitLean leanHit, int idx))
            return null;
        return _triangles[idx].Inflate(leanHit);
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
