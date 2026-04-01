using System;

class Mesh : IShape
{
    private Triangle[] _triangles;
    private Bvh<Triangle> _bvh;
    private Counters? _counters;

    public Mesh(Triangle[] triangles, Counters? counters = null)
    {
        _triangles = triangles;
        _bvh = new Bvh<Triangle>(_triangles);
        _counters = counters;

        counters?.Bump(Counters.Type.MeshTriangle, triangles.Length);
    }

    public AABox Bounds() => _bvh.Bounds;
    public bool Overlaps(AABox box) => _bvh.Overlaps(box);
    public RayHit? Intersect(Ray ray)
    {
        _counters?.Bump(Counters.Type.MeshIntersect);
        return _bvh.Intersect(ray)?.Hit;
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

    public void Describe(ref FormatWriter w)
    {
        BvhStats bvhStats = _bvh.GetStats();
        w.WriteLine($"tris={_triangles.Length}");
        w.WriteLine($"nodes={bvhStats.Nodes}");
        w.WriteLine($"leaves={bvhStats.Leaves}");
        w.WriteLine($"depth={bvhStats.MaxDepth}");
    }
}
