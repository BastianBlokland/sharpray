using System;

class Mesh : IShape
{
    private Triangle[] _triangles;
    private AABox _bounds;

    public Mesh(params Triangle[] triangles)
    {
        _triangles = triangles;

        _bounds = AABox.Inverted();
        foreach (Triangle tri in _triangles)
        {
            _bounds.Encapsulate(tri.Bounds());
        }
    }

    public AABox Bounds() => _bounds;

    public bool Overlaps(AABox box)
    {
        foreach (Triangle tri in _triangles)
        {
            if (tri.Overlaps(box))
                return true;
        }
        return false;
    }

    public RayHit? Intersect(Ray ray)
    {
        RayHit? closest = null;
        foreach (Triangle tri in _triangles)
        {
            if (tri.Intersect(ray) is RayHit hit && (closest is null || hit.Dist < closest.Value.Dist))
                closest = hit;
        }
        return closest;
    }

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
