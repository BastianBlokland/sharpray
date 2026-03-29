using System;
using System.Collections.Generic;

class Mesh : IShape
{
    private IReadOnlyList<Triangle> _triangles;
    private Bvh<Triangle> _bvh;

    public Mesh(IReadOnlyList<Triangle> triangles)
    {
        _triangles = triangles;
        _bvh = new Bvh<Triangle>(_triangles);
    }

    public AABox Bounds() => _bvh.Bounds;
    public bool Overlaps(AABox box) => _bvh.Overlaps(box);
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

    public void OverlayStats(Overlay overlay, Transform trans, Color color)
    {
        BvhStats bvhStats = _bvh.GetStats();
        string text = $"""
            tris:   {_triangles.Count}
            nodes:  {bvhStats.Nodes}
            leaves: {bvhStats.Leaves}
            depth:  {bvhStats.MaxDepth}
            """;
        overlay.AddText(text, trans.TransformPoint(_bvh.Bounds.Center), color);
    }
}
