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
}
