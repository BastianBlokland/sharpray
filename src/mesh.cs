using System;

class Mesh : IShape
{
    private Triangle[] _triangles;
    private AABox _bounds;
    private Bvh<Triangle> _bvh;

    public Mesh(params Triangle[] triangles)
    {
        _triangles = triangles;
        _bvh = new Bvh<Triangle>(_triangles);

        _bounds = AABox.Inverted();
        foreach (Triangle tri in _triangles)
        {
            _bounds.Encapsulate(tri.Bounds());
        }
    }

    public AABox Bounds() => _bounds;

    public bool Overlaps(AABox box)
    {
        if (!_bounds.Overlaps(box))
            return false;

        foreach (Triangle tri in _triangles)
        {
            if (tri.Overlaps(box))
                return true;
        }

        return false;
    }

    public RayHit? Intersect(Ray ray) => _bvh.Intersect(ray)?.Hit;

    public void OverlayBounds(Overlay overlay, Transform trans, int maxDepth = int.MaxValue) =>
        _bvh.OverlayBounds(overlay, trans, maxDepth);

    public void OverlayWireframe(Overlay overlay, Transform trans, Color color)
    {
        foreach (Triangle tri in _triangles)
        {
            Vec3 a = trans.TransformPoint(tri.A);
            Vec3 b = trans.TransformPoint(tri.B);
            Vec3 c = trans.TransformPoint(tri.C);

            overlay.AddLine(new Line(a, b), color);
            overlay.AddLine(new Line(b, c), color);
            overlay.AddLine(new Line(c, a), color);
        }
    }
}
