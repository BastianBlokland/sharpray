using System;
using System.Collections.Generic;

class Mesh : IShape
{
    private struct TriangleAttributes
    {
        public Vec3 NormalA, NormalB, NormalC;
        public Vec4 TangentA, TangentB, TangentC; // xyz = tangent direction, w = bitangent handedness (+1/-1).
        public Vec2 UvA, UvB, UvC;
    }

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
                NormalA = triangles[i].NormA,
                NormalB = triangles[i].NormB,
                NormalC = triangles[i].NormC,
                TangentA = triangles[i].TanA,
                TangentB = triangles[i].TanB,
                TangentC = triangles[i].TanC,
                UvA = triangles[i].UvA,
                UvB = triangles[i].UvB,
                UvC = triangles[i].UvC,
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
            _attributes[idx].NormalA, _attributes[idx].NormalB, _attributes[idx].NormalC,
            _attributes[idx].TangentA, _attributes[idx].TangentB, _attributes[idx].TangentC,
            _attributes[idx].UvA, _attributes[idx].UvB, _attributes[idx].UvC);
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

    public static void ComputeSmoothNormals(Span<Triangle> triangles)
    {
        const float posEqEpsilon = 1e-6f;
        var normalAccum = new Dictionary<Vec3, Vec3>(new Vec3Comparer(posEqEpsilon));

        // Accumulate weighted normals for each vertex.
        foreach (Triangle tri in triangles)
        {
            Vec3 normWeighted = tri.Normal * tri.Area;
            normalAccum[tri.PosA] = normalAccum.GetValueOrDefault(tri.PosA) + normWeighted;
            normalAccum[tri.PosB] = normalAccum.GetValueOrDefault(tri.PosB) + normWeighted;
            normalAccum[tri.PosC] = normalAccum.GetValueOrDefault(tri.PosC) + normWeighted;
        }

        // Update the triangles based on the accumulated vertex normals.
        for (int i = 0; i != triangles.Length; ++i)
        {
            ref Triangle tri = ref triangles[i];

            Vec3 normA = normalAccum[tri.PosA].NormalizeOr(tri.Normal);
            Vec3 normB = normalAccum[tri.PosB].NormalizeOr(tri.Normal);
            Vec3 normC = normalAccum[tri.PosC].NormalizeOr(tri.Normal);
            tri = new Triangle(tri.PosA, tri.PosB, tri.PosC, normA, normB, normC, tri.UvA, tri.UvB, tri.UvC);
        }
    }

    public static Mesh CreateSphere(int rings = 32, int segments = 32, float radius = 1f)
    {
        var triangles = new List<Triangle>();

        for (int ring = 0; ring != rings; ++ring)
        {
            float theta0 = MathF.PI * ring / rings;
            float theta1 = MathF.PI * (ring + 1) / rings;
            float v0 = (float)ring / rings;
            float v1 = (float)(ring + 1) / rings;

            for (int seg = 0; seg != segments; ++seg)
            {
                float phi0 = MathF.PI + 2f * MathF.PI * seg / segments;
                float phi1 = MathF.PI + 2f * MathF.PI * (seg + 1) / segments;
                float u0 = (float)seg / segments;
                float u1 = (float)(seg + 1) / segments;

                Vec3 n00 = SphereNormal(theta0, phi0);
                Vec3 n01 = SphereNormal(theta0, phi1);
                Vec3 n10 = SphereNormal(theta1, phi0);
                Vec3 n11 = SphereNormal(theta1, phi1);

                Vec3 p00 = n00 * radius;
                Vec3 p01 = n01 * radius;
                Vec3 p10 = n10 * radius;
                Vec3 p11 = n11 * radius;

                Vec2 uv00 = new Vec2(u0, v0);
                Vec2 uv01 = new Vec2(u1, v0);
                Vec2 uv10 = new Vec2(u0, v1);
                Vec2 uv11 = new Vec2(u1, v1);

                // Two triangles per quad, wound CCW from outside.
                triangles.Add(new Triangle(p00, p11, p10, n00, n11, n10, uv00, uv11, uv10));
                triangles.Add(new Triangle(p00, p01, p11, n00, n01, n11, uv00, uv01, uv11));
            }
        }

        return new Mesh(triangles);
    }

    private static Vec3 SphereNormal(float theta, float phi)
    {
        float sinTheta = MathF.Sin(theta);
        return new Vec3(sinTheta * MathF.Cos(phi), MathF.Cos(theta), sinTheta * MathF.Sin(phi));
    }
}
