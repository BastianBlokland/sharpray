using System;
using System.Collections.Generic;

class Mesh : IShape
{
    private struct TriangleAttributes { public Vec3 NormalA, NormalB, NormalC; }

    private TriangleLean[] _triangles;
    private TriangleAttributes[] _attributes;
    private Bvh<TriangleLean, ShapeHitLean> _bvh;
    private Counters? _counters;

    public Mesh(IReadOnlyList<Triangle> triangles, Counters? counters = null)
    {
        _triangles = new TriangleLean[triangles.Count];
        _attributes = new TriangleAttributes[triangles.Count];
        for (int i = 0; i < triangles.Count; i++)
        {
            _triangles[i] = triangles[i].Lean;
            _attributes[i] = new TriangleAttributes
            {
                NormalA = triangles[i].NormalA,
                NormalB = triangles[i].NormalB,
                NormalC = triangles[i].NormalC,
            };
        }
        using (counters?.TimeScope(Counters.Type.TimeMeshBvhBuild))
        {
            const float sahCostIntersect = 0.5f; // sahCostIntersect low as the triangle test is cheap.
            _bvh = new Bvh<TriangleLean, ShapeHitLean>(_triangles, sahCostIntersect: sahCostIntersect);
        }
        _counters = counters;

        counters?.Bump(Counters.Type.MeshTriangle, triangles.Count);
    }

    public AABox Bounds() => _bvh.Bounds;
    public bool Overlaps(AABox box) => _bvh.Overlaps(box, _counters);

    public ShapeHit? Intersect(Ray ray)
    {
        _counters?.Bump(Counters.Type.MeshIntersect);
        if (_bvh.Intersect(ray, _counters) is not (ShapeHitLean leanHit, int idx))
            return null;

        return _triangles[idx].Inflate(
            leanHit,
            _attributes[idx].NormalA,
            _attributes[idx].NormalB,
            _attributes[idx].NormalC);
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
        foreach (TriangleLean tri in _triangles)
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
