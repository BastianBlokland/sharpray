using System;
using System.Diagnostics;
using System.Numerics;

interface IShape
{
    AABox Bounds();
    bool Overlaps(AABox box);
    RayHit? Intersect(Ray ray);
}

static class ShapeExtensions
{
    public static RayHit? Intersect(this IShape shape, Ray ray, Transform trans)
    {
        var (localRay, localRayScale) = trans.TransformRayInv(ray);
        if (shape.Intersect(localRay) is RayHit hit)
        {
            Vec3 worldNorm = (trans.Rot * (hit.Norm / trans.Scale)).Normalize();
            return new RayHit(hit.Dist / localRayScale, worldNorm);
        }
        return null;
    }
}

struct Color
{
    public float R, G, B;

    public Color(float v)
    {
        R = v;
        G = v;
        B = v;
    }

    public Color(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
    }

    public float this[int i]
    {
        get
        {
            Debug.Assert(i >= 0 && i < 3);
            return i switch { 0 => R, 1 => G, _ => B };
        }
    }

    public Color Clamp01() => new Color(Math.Clamp(R, 0f, 1f), Math.Clamp(G, 0f, 1f), Math.Clamp(B, 0f, 1f));

    public Pixel ToPixel()
    {
        // SRGB encode.
        // http://chilliant.blogspot.com/2012/08/srgb-approximations-for-hlsl.html
        Color c = Clamp01();
        float r = MathF.Max(1.055f * MathF.Pow(c.R, 0.416666667f) - 0.055f, 0f);
        float g = MathF.Max(1.055f * MathF.Pow(c.G, 0.416666667f) - 0.055f, 0f);
        float b = MathF.Max(1.055f * MathF.Pow(c.B, 0.416666667f) - 0.055f, 0f);
        return new Pixel((byte)(r * 255f + 0.5f), (byte)(g * 255f + 0.5f), (byte)(b * 255f + 0.5f));
    }

    public static Color operator +(Color a, Color b) => new Color(a.R + b.R, a.G + b.G, a.B + b.B);
    public static Color operator -(Color a, Color b) => new Color(a.R - b.R, a.G - b.G, a.B - b.B);
    public static Color operator *(Color a, Color b) => new Color(a.R * b.R, a.G * b.G, a.B * b.B);
    public static Color operator *(Color c, float s) => new Color(c.R * s, c.G * s, c.B * s);
    public static Color operator *(float s, Color c) => new Color(c.R * s, c.G * s, c.B * s);

    public static Color operator /(Color c, float s)
    {
        Debug.Assert(s != 0f);
        return new Color(c.R / s, c.G / s, c.B / s);
    }

    public static Color operator /(Color a, Color b)
    {
        Debug.Assert(b.R != 0f && b.G != 0f && b.B != 0f);
        return new Color(a.R / b.R, a.G / b.G, a.B / b.B);
    }

    public static Color Black => new Color(0f);
    public static Color White => new Color(1f);
    public static Color Gray => new Color(0.5f);
    public static Color Red => new Color(1f, 0f, 0f);
    public static Color Green => new Color(0f, 1f, 0f);
    public static Color Blue => new Color(0f, 0f, 1f);
    public static Color Yellow => new Color(1f, 1f, 0f);
    public static Color Cyan => new Color(0f, 1f, 1f);
    public static Color Magenta => new Color(1f, 0f, 1f);

    public static Color Min(Color a, Color b) => new Color(MathF.Min(a.R, b.R), MathF.Min(a.G, b.G), MathF.Min(a.B, b.B));
    public static Color Max(Color a, Color b) => new Color(MathF.Max(a.R, b.R), MathF.Max(a.G, b.G), MathF.Max(a.B, b.B));

    public static Color Lerp(Color a, Color b, float t) => new Color(
        a.R + (b.R - a.R) * t,
        a.G + (b.G - a.G) * t,
        a.B + (b.B - a.B) * t);

    public static Color Bilerp(Color c1, Color c2, Color c3, Color c4, float tX, float tY) =>
        Lerp(Lerp(c1, c2, tX), Lerp(c3, c4, tX), tY);

    public static Color FromPixel(Pixel pixel)
    {
        // SRGB decode.
        float r = MathF.Pow(pixel.R / 255f, 2.233333333f);
        float g = MathF.Pow(pixel.G / 255f, 2.233333333f);
        float b = MathF.Pow(pixel.B / 255f, 2.233333333f);
        return new Color(r, g, b);
    }
}

struct Vec2
{
    public float X, Y;

    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float this[int i]
    {
        get
        {
            Debug.Assert(i >= 0 && i < 2);
            return i switch { 0 => X, _ => Y };
        }
    }

    public Vec2i ToInt() => new Vec2i((int)MathF.Round(X), (int)MathF.Round(Y));

    public static Vec2 operator -(Vec2 v) => new Vec2(-v.X, -v.Y);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, Vec2 b) => new Vec2(a.X * b.X, a.Y * b.Y);
    public static Vec2 operator *(Vec2 v, float s) => new Vec2(v.X * s, v.Y * s);
    public static Vec2 operator *(float s, Vec2 v) => new Vec2(s * v.X, s * v.Y);
    public static Vec2 operator /(Vec2 v, float s)
    {
        Debug.Assert(s != 0f);
        return new Vec2(v.X / s, v.Y / s);
    }

    public static Vec2 Zero => new Vec2(0f, 0f);
}

struct Vec2i
{
    public int X, Y;

    public Vec2i(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int this[int i]
    {
        get
        {
            Debug.Assert(i >= 0 && i < 2);
            return i switch { 0 => X, _ => Y };
        }
    }

    public Vec2 ToFloat() => new Vec2(X, Y);

    public static Vec2i operator -(Vec2i v) => new Vec2i(-v.X, -v.Y);
    public static Vec2i operator +(Vec2i a, Vec2i b) => new Vec2i(a.X + b.X, a.Y + b.Y);
    public static Vec2i operator -(Vec2i a, Vec2i b) => new Vec2i(a.X - b.X, a.Y - b.Y);
    public static Vec2i operator *(Vec2i v, int s) => new Vec2i(v.X * s, v.Y * s);
    public static Vec2i operator *(int s, Vec2i v) => new Vec2i(s * v.X, s * v.Y);

    public static Vec2i Zero => new Vec2i(0, 0);
}

struct Vec3
{
    public float X, Y, Z;

    public Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Vec3(Vec2 xy, float z)
    {
        X = xy.X;
        Y = xy.Y;
        Z = z;
    }

    public float this[int i]
    {
        get
        {
            Debug.Assert(i >= 0 && i < 3);
            return i switch { 0 => X, 1 => Y, _ => Z };
        }
    }

    public float MagnitudeSqr() => Dot(this, this);
    public float Magnitude() => MathF.Sqrt(MagnitudeSqr());
    public bool IsUnit => MathF.Abs(MagnitudeSqr() - 1f) < 1e-4f;
    public bool IsOne => MathF.Abs(X - 1f) < 1e-4f && MathF.Abs(Y - 1f) < 1e-4f && MathF.Abs(Z - 1f) < 1e-4f;

    public Vec3 Normalize()
    {
        float m = Magnitude();
        Debug.Assert(m >= 1e-6f, "Cannot normalize a zero vector");
        return this / m;
    }

    public Vec3 NormalizeOr(Vec3 fallback)
    {
        float mSqr = MagnitudeSqr();
        return mSqr >= 1e-12f ? this / MathF.Sqrt(mSqr) : fallback;
    }

    public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, float b) => new Vec3(a.X * b, a.Y * b, a.Z * b);
    public static Vec3 operator *(float a, Vec3 b) => new Vec3(a * b.X, a * b.Y, a * b.Z);
    public static Vec3 operator *(Vec3 a, Vec3 b) => new Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vec3 operator /(Vec3 a, float b)
    {
        Debug.Assert(b != 0f);
        return new Vec3(a.X / b, a.Y / b, a.Z / b);
    }
    public static Vec3 operator /(Vec3 a, Vec3 b)
    {
        Debug.Assert(b.X != 0f && b.Y != 0f && b.Z != 0f);
        return new Vec3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    }

    public static Vec3 Zero => new Vec3(0, 0, 0);
    public static Vec3 Up => new Vec3(0, 1, 0);
    public static Vec3 Down => new Vec3(0, -1, 0);
    public static Vec3 Right => new Vec3(1, 0, 0);
    public static Vec3 Left => new Vec3(-1, 0, 0);
    public static Vec3 Forward => new Vec3(0, 0, 1);
    public static Vec3 Back => new Vec3(0, 0, -1);

    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static Vec3 Project(Vec3 v, Vec3 n)
    {
        float nSqrMag = n.MagnitudeSqr();
        Debug.Assert(nSqrMag >= 1e-6f, "Cannot project onto a zero vector");
        return n * (Dot(v, n) / nSqrMag);
    }

    public static Vec3 Reflect(Vec3 v, Vec3 n)
    {
        Debug.Assert(n.IsUnit, "Reflect normal must be normalized");
        return v - 2 * Dot(v, n) * n;
    }

    public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => new Vec3(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t);

    public static Vec3 RandOnSphere(ref Rng rng)
    {
        while (true)
        {
            var (x, y) = rng.NextGauss();
            var (z, _) = rng.NextGauss();
            float magSqr = x * x + y * y + z * z;
            if (magSqr > 1e-6f)
                return new Vec3(x, y, z) / MathF.Sqrt(magSqr);
        }
    }

    public static Vec3 RandOnHemiSphere(ref Rng rng, Vec3 normal)
    {
        Debug.Assert(normal.IsUnit, "Hemisphere normal must be normalized");
        Vec3 v = RandOnSphere(ref rng);
        return Vec3.Dot(v, normal) < 0f ? -v : v;
    }

    public static Vec3 RandInSphere(ref Rng rng)
    {
        // Cube-root scales uniformly since volume grows cubically with radius.
        return RandOnSphere(ref rng) * MathF.Cbrt(rng.NextFloat());
    }

    public static Vec3 RandInCone(ref Rng rng, float coneAngleRad)
    {
        Debug.Assert(coneAngleRad >= 0f && coneAngleRad <= MathF.PI);
        // http://www.realtimerendering.com/resources/RTNews/html/rtnv20n1.html#art11
        float cosAngle = MathF.Cos(coneAngleRad);
        float phi = MathF.PI * 2f * rng.NextFloat();
        float z = cosAngle + (1f - cosAngle) * rng.NextFloat();
        float sinT = MathF.Sqrt(1f - z * z);
        return new Vec3(MathF.Cos(phi) * sinT, MathF.Sin(phi) * sinT, z);
    }
}

struct Vec4
{
    public float X, Y, Z, W;

    public Vec4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public Vec4(Vec3 xyz, float w)
    {
        X = xyz.X;
        Y = xyz.Y;
        Z = xyz.Z;
        W = w;
    }

    public float this[int i]
    {
        get
        {
            Debug.Assert(i >= 0 && i < 4);
            return i switch { 0 => X, 1 => Y, 2 => Z, _ => W };
        }
    }

    public static Vec4 operator -(Vec4 v) => new Vec4(-v.X, -v.Y, -v.Z, -v.W);
    public static Vec4 operator +(Vec4 a, Vec4 b) => new Vec4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vec4 operator -(Vec4 a, Vec4 b) => new Vec4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vec4 operator *(Vec4 a, float b) => new Vec4(a.X * b, a.Y * b, a.Z * b, a.W * b);
    public static Vec4 operator *(float a, Vec4 b) => new Vec4(a * b.X, a * b.Y, a * b.Z, a * b.W);
    public static Vec4 operator /(Vec4 a, float b)
    {
        Debug.Assert(b != 0f);
        return new Vec4(a.X / b, a.Y / b, a.Z / b, a.W / b);
    }

    public static float Dot(Vec4 a, Vec4 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
}

struct Quat
{
    public float X, Y, Z, W;

    private Quat(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public float this[int i]
    {
        get
        {
            Debug.Assert(i >= 0 && i < 4);
            return i switch { 0 => X, 1 => Y, 2 => Z, _ => W };
        }
    }

    public bool IsUnit => MathF.Abs(MagnitudeSqr() - 1f) < 1e-4f;

    public float MagnitudeSqr() => X * X + Y * Y + Z * Z + W * W;
    public Quat Inverse() => new Quat(-X, -Y, -Z, W);

    public Quat Normalize()
    {
        float mag = MathF.Sqrt(X * X + Y * Y + Z * Z + W * W);
        Debug.Assert(mag >= 1e-6f, "Cannot normalize a zero quaternion");
        float magInv = 1.0f / mag;
        return new Quat(X * magInv, Y * magInv, Z * magInv, W * magInv);
    }

    public static Quat operator *(Quat a, Quat b) => new Quat(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
        a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public static Vec3 operator *(Quat q, Vec3 v)
    {
        Vec3 axis = new Vec3(q.X, q.Y, q.Z);
        float sqrMag = axis.MagnitudeSqr();
        Vec3 a = axis * (Vec3.Dot(axis, v) * 2);
        Vec3 b = v * (q.W * q.W - sqrMag);
        Vec3 c = Vec3.Cross(axis, v) * (q.W * 2);
        return a + b + c;
    }

    public static Quat Identity() => new Quat(0, 0, 0, 1);

    public static Quat AngleAxis(float angle, Vec3 axis)
    {
        Debug.Assert(axis.IsUnit, "AngleAxis axis must be normalized");
        Vec3 vec = axis * MathF.Sin(angle * 0.5f);
        return new Quat(vec.X, vec.Y, vec.Z, MathF.Cos(angle * 0.5f));
    }

    public static Quat FromTo(Quat from, Quat to) => to * from.Inverse();

    public static float Dot(Quat a, Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    public static Quat FromMat4(Mat4 m)
    {
        // https://www.euclideanspace.com/maths/geometry/rotations/conversions/matrixToQuaternion/
        float trace = m.C0.X + m.C1.Y + m.C2.Z;
        if (trace > 1e-6f)
        {
            float s = MathF.Sqrt(trace + 1) * 2; // s = 4w
            return new Quat((m.C1.Z - m.C2.Y) / s, (m.C2.X - m.C0.Z) / s, (m.C0.Y - m.C1.X) / s, s * 0.25f);
        }
        if (m.C0.X > m.C1.Y && m.C0.X > m.C2.Z)
        {
            float s = MathF.Sqrt(1 + m.C0.X - m.C1.Y - m.C2.Z) * 2; // s = 4x
            return new Quat(s * 0.25f, (m.C1.X + m.C0.Y) / s, (m.C2.X + m.C0.Z) / s, (m.C1.Z - m.C2.Y) / s);
        }
        if (m.C1.Y > m.C2.Z)
        {
            float s = MathF.Sqrt(1 + m.C1.Y - m.C0.X - m.C2.Z) * 2; // s = 4y
            return new Quat((m.C1.X + m.C0.Y) / s, s * 0.25f, (m.C2.Y + m.C1.Z) / s, (m.C2.X - m.C0.Z) / s);
        }
        else
        {
            float s = MathF.Sqrt(1 + m.C2.Z - m.C0.X - m.C1.Y) * 2; // s = 4z
            return new Quat((m.C2.X + m.C0.Z) / s, (m.C2.Y + m.C1.Z) / s, s * 0.25f, (m.C0.Y - m.C1.X) / s);
        }
    }

    public static Quat Look(Vec3 forward, Vec3 upRef) => FromMat4(Mat4.RotateLook(forward, upRef));
}

struct Mat4
{
    public Vec4 C0, C1, C2, C3; // Column-major.

    private Mat4(Vec4 c0, Vec4 c1, Vec4 c2, Vec4 c3)
    {
        C0 = c0; C1 = c1; C2 = c2; C3 = c3;
    }

    public Vec4 Row(int i) => new Vec4(C0[i], C1[i], C2[i], C3[i]);

    public Vec3 TransformDir(Vec3 v)
    {
        Vec4 v4 = new Vec4(v, 0);
        return new Vec3(Vec4.Dot(Row(0), v4), Vec4.Dot(Row(1), v4), Vec4.Dot(Row(2), v4));
    }

    public Vec3 TransformPoint(Vec3 v)
    {
        Vec4 v4 = new Vec4(v, 1);
        return new Vec3(Vec4.Dot(Row(0), v4), Vec4.Dot(Row(1), v4), Vec4.Dot(Row(2), v4));
    }

    public static Mat4 Identity() => new Mat4(
        new Vec4(1, 0, 0, 0),
        new Vec4(0, 1, 0, 0),
        new Vec4(0, 0, 1, 0),
        new Vec4(0, 0, 0, 1));

    public static Mat4 operator *(Mat4 a, Mat4 b) => new Mat4(
        new Vec4(Vec4.Dot(a.Row(0), b.C0), Vec4.Dot(a.Row(1), b.C0), Vec4.Dot(a.Row(2), b.C0), Vec4.Dot(a.Row(3), b.C0)),
        new Vec4(Vec4.Dot(a.Row(0), b.C1), Vec4.Dot(a.Row(1), b.C1), Vec4.Dot(a.Row(2), b.C1), Vec4.Dot(a.Row(3), b.C1)),
        new Vec4(Vec4.Dot(a.Row(0), b.C2), Vec4.Dot(a.Row(1), b.C2), Vec4.Dot(a.Row(2), b.C2), Vec4.Dot(a.Row(3), b.C2)),
        new Vec4(Vec4.Dot(a.Row(0), b.C3), Vec4.Dot(a.Row(1), b.C3), Vec4.Dot(a.Row(2), b.C3), Vec4.Dot(a.Row(3), b.C3)));

    public static Mat4 Translate(Vec3 t) => new Mat4(
        new Vec4(1, 0, 0, 0),
        new Vec4(0, 1, 0, 0),
        new Vec4(0, 0, 1, 0),
        new Vec4(t.X, t.Y, t.Z, 1));

    public static Mat4 Scale(Vec3 s) => new Mat4(
        new Vec4(s.X, 0, 0, 0),
        new Vec4(0, s.Y, 0, 0),
        new Vec4(0, 0, s.Z, 0),
        new Vec4(0, 0, 0, 1));

    public static Mat4 RotateX(float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Mat4(
            new Vec4(1, 0, 0, 0),
            new Vec4(0, c, s, 0),
            new Vec4(0, -s, c, 0),
            new Vec4(0, 0, 0, 1));
    }

    public static Mat4 RotateY(float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Mat4(
            new Vec4(c, 0, -s, 0),
            new Vec4(0, 1, 0, 0),
            new Vec4(s, 0, c, 0),
            new Vec4(0, 0, 0, 1));
    }

    public static Mat4 RotateZ(float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Mat4(
            new Vec4(c, s, 0, 0),
            new Vec4(-s, c, 0, 0),
            new Vec4(0, 0, 1, 0),
            new Vec4(0, 0, 0, 1));
    }

    public static Mat4 RotateLook(Vec3 forward, Vec3 upRef)
    {
        if (forward.MagnitudeSqr() <= 1e-6f) return Identity();
        if (upRef.MagnitudeSqr() <= 1e-6f) return Identity();

        Vec3 fwd = forward.Normalize();
        Vec3 right = Vec3.Cross(upRef, fwd);
        right = right.MagnitudeSqr() > 1e-6f ? right.Normalize() : new Vec3(1, 0, 0);
        Vec3 up = Vec3.Cross(fwd, right);

        return new Mat4(
            new Vec4(right.X, right.Y, right.Z, 0),
            new Vec4(up.X, up.Y, up.Z, 0),
            new Vec4(fwd.X, fwd.Y, fwd.Z, 0),
            new Vec4(0, 0, 0, 1));
    }

    public static Mat4 RotateQuat(Quat q)
    {
        float x = q.X, y = q.Y, z = q.Z, w = q.W;
        return new Mat4(
            new Vec4(1 - 2 * y * y - 2 * z * z, 2 * x * y + 2 * w * z, 2 * x * z - 2 * w * y, 0),
            new Vec4(2 * x * y - 2 * w * z, 1 - 2 * x * x - 2 * z * z, 2 * y * z + 2 * w * x, 0),
            new Vec4(2 * x * z + 2 * w * y, 2 * y * z - 2 * w * x, 1 - 2 * x * x - 2 * y * y, 0),
            new Vec4(0, 0, 0, 1));
    }

    public static Mat4 Trs(Vec3 t, Quat r, Vec3 s)
    {
        Mat4 m = RotateQuat(r);
        m.C0 = m.C0 * s.X;
        m.C1 = m.C1 * s.Y;
        m.C2 = m.C2 * s.Z;
        m.C3 = new Vec4(t.X, t.Y, t.Z, 1);
        return m;
    }
}


struct Transform
{
    public Vec3 Pos;
    public Quat Rot;
    public Vec3 Scale;

    public Transform(Vec3 pos, Quat rot, Vec3 scale)
    {
        Debug.Assert(rot.IsUnit, "Transform rotation must be normalized");
        Debug.Assert(scale.X != 0f && scale.Y != 0f && scale.Z != 0f, "Scale cannot be zero");
        Pos = pos;
        Rot = rot;
        Scale = scale;
    }

    public Transform(Vec3 pos, Quat rot)
    {
        Debug.Assert(rot.IsUnit, "Transform rotation must be normalized");
        Pos = pos;
        Rot = rot;
        Scale = new Vec3(1, 1, 1);
    }

    public Transform(Vec3 pos)
    {
        Pos = pos;
        Rot = Quat.Identity();
        Scale = new Vec3(1, 1, 1);
    }

    public void RotateAround(Vec3 pivot, Quat rot)
    {
        Pos = pivot + rot * (Pos - pivot);
        Rot = (rot * Rot).Normalize();
    }

    public Vec3 TransformDir(Vec3 d) => Rot * d;
    public Vec3 TransformDirInv(Vec3 d) => Rot.Inverse() * d;

    public Vec3 TransformVector(Vec3 v) => Rot * (v * Scale);
    public Vec3 TransformVectorInv(Vec3 v) => Rot.Inverse() * v / Scale;

    public Vec3 TransformPoint(Vec3 p) => Pos + Rot * (p * Scale);
    public Vec3 TransformPointInv(Vec3 p) => Rot.Inverse() * (p - Pos) / Scale;

    public Ray TransformRay(Ray ray) => new Ray(TransformPoint(ray.Origin), TransformDir(ray.Dir));
    public (Ray Ray, float Scale) TransformRayInv(Ray ray)
    {
        Vec3 origin = TransformPointInv(ray.Origin);
        Vec3 dirRaw = TransformVectorInv(ray.Dir);
        float scale = dirRaw.Magnitude();
        return (new Ray(origin, dirRaw / scale), scale);
    }

    public Transform Inverse()
    {
        Vec3 invScale = new Vec3(1f / Scale.X, 1f / Scale.Y, 1f / Scale.Z);
        Quat invRot = Rot.Inverse();
        Vec3 invPos = invRot * (-Pos * invScale);
        return new Transform(invPos, invRot, invScale);
    }

    public Mat4 ToMat() => Mat4.Trs(Pos, Rot, Scale);

    public static Transform Identity() => new Transform(new Vec3(0, 0, 0), Quat.Identity(), new Vec3(1, 1, 1));

    public static Transform operator *(Transform a, Transform b) => new Transform(
        a.TransformPoint(b.Pos),
        a.Rot * b.Rot,
        a.Scale * b.Scale);
}

struct Ray
{
    public Vec3 Origin, Dir;

    public Ray(Vec3 origin, Vec3 dir)
    {
        Debug.Assert(dir.IsUnit, "Ray direction must be normalized");
        Origin = origin;
        Dir = dir;
    }

    public Vec3 this[float t] => Origin + Dir * t;

    public float Distance(Vec3 p) => Vec3.Dot(p - Origin, Dir);
}

struct RayHit
{
    public float Dist;
    public Vec3 Norm;

    public RayHit(float dist, Vec3 norm)
    {
        Debug.Assert(norm.IsUnit, "RayHit normal must be normalized");
        Dist = dist;
        Norm = norm;
    }
}

struct Line
{
    public Vec3 A, B;

    public Line(Vec3 a, Vec3 b)
    {
        A = a;
        B = b;
    }

    public float LengthSqr => (B - A).MagnitudeSqr();
    public float Length => MathF.Sqrt(LengthSqr);

    public Vec3 Direction
    {
        get
        {
            Vec3 delta = B - A;
            float length = delta.Magnitude();
            return length <= 1e-6f ? new Vec3(0, 0, 1) : delta / length;
        }
    }

    public float ClosestTime(Vec3 point)
    {
        Vec3 toB = B - A;
        float lengthSqr = toB.MagnitudeSqr();
        if (lengthSqr < 1e-6f) return 0f;
        return Math.Clamp(Vec3.Dot(point - A, toB) / lengthSqr, 0f, 1f);
    }

    public float ClosestTime(Ray ray)
    {
        float lineLength = Length;
        if (lineLength < 1e-6f) return 0f;

        Vec3 lineDir = Direction;
        float dot = Vec3.Dot(lineDir, ray.Dir);
        float d = 1f - dot * dot;
        if (d < 1e-6f) return 0f; // Parallel.

        Vec3 toA = A - ray.Origin;
        float c = Vec3.Dot(lineDir, toA);
        float f = Vec3.Dot(ray.Dir, toA);
        float dist = (dot * f - c) / d;
        if (dist <= 0f) return 0f;

        return dist >= lineLength ? 1f : dist / lineLength;
    }

    public Vec3 ClosestPoint(Vec3 point) => Vec3.Lerp(A, B, ClosestTime(point));
    public Vec3 ClosestPoint(Ray ray) => Vec3.Lerp(A, B, ClosestTime(ray));
    public float DistanceSqr(Vec3 point) => (point - ClosestPoint(point)).MagnitudeSqr();
}

struct AABox : IShape
{
    public Vec3 Min, Max;

    public AABox(Vec3 min, Vec3 max)
    {
        Min = min;
        Max = max;
    }

    public Vec3 Center => (Min + Max) * 0.5f;
    public Vec3 Size => Max - Min;
    public bool IsInverted => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public bool Contains(Vec3 p) =>
        p.X > Min.X && p.X < Max.X &&
        p.Y > Min.Y && p.Y < Max.Y &&
        p.Z > Min.Z && p.Z < Max.Z;

    public Vec3 ClosestPoint(Vec3 p) => new Vec3(
        Math.Clamp(p.X, Min.X, Max.X),
        Math.Clamp(p.Y, Min.Y, Max.Y),
        Math.Clamp(p.Z, Min.Z, Max.Z));

    public void Encapsulate(Vec3 p)
    {
        Min = new Vec3(MathF.Min(Min.X, p.X), MathF.Min(Min.Y, p.Y), MathF.Min(Min.Z, p.Z));
        Max = new Vec3(MathF.Max(Max.X, p.X), MathF.Max(Max.Y, p.Y), MathF.Max(Max.Z, p.Z));
    }

    public void Encapsulate(AABox other)
    {
        Min = new Vec3(MathF.Min(Min.X, other.Min.X), MathF.Min(Min.Y, other.Min.Y), MathF.Min(Min.Z, other.Min.Z));
        Max = new Vec3(MathF.Max(Max.X, other.Max.X), MathF.Max(Max.Y, other.Max.Y), MathF.Max(Max.Z, other.Max.Z));
    }

    public Box Transform(Transform trans) =>
        new Box(AABox.FromCenter(trans.TransformPoint(Center), Size * trans.Scale), trans.Rot);

    public AABox Bounds() => this;

    public bool Overlaps(AABox other) =>
        Min.X < other.Max.X && Max.X > other.Min.X &&
        Min.Y < other.Max.Y && Max.Y > other.Min.Y &&
        Min.Z < other.Max.Z && Max.Z > other.Min.Z;

    public RayHit? Intersect(Ray ray)
    {
        // Cyrus-Beck slab clipping.
        // https://izzofinal.wordpress.com/2012/11/09/ray-vs-box-round-1/
        float dirXInv = 1f / ray.Dir.X;
        float dirYInv = 1f / ray.Dir.Y;
        float dirZInv = 1f / ray.Dir.Z;

        float t1 = (Min.X - ray.Origin.X) * dirXInv;
        float t2 = (Max.X - ray.Origin.X) * dirXInv;
        float t3 = (Min.Y - ray.Origin.Y) * dirYInv;
        float t4 = (Max.Y - ray.Origin.Y) * dirYInv;
        float t5 = (Min.Z - ray.Origin.Z) * dirZInv;
        float t6 = (Max.Z - ray.Origin.Z) * dirZInv;

        float minA = MathF.Min(t1, t2), minB = MathF.Min(t3, t4), minC = MathF.Min(t5, t6);
        float maxA = MathF.Max(t1, t2), maxB = MathF.Max(t3, t4), maxC = MathF.Max(t5, t6);

        float tMin = MathF.Max(MathF.Max(minA, minB), minC);
        float tMax = MathF.Min(MathF.Min(maxA, maxB), maxC);

        if (tMax < 0f || tMin > tMax) return null;

        bool inside = tMin < 0f;
        float t = inside ? tMax : tMin;

        Vec3 norm;
        if (minA >= minB && minA >= minC)
            norm = t1 <= t2 ? new Vec3(-1, 0, 0) : new Vec3(1, 0, 0);
        else if (minB >= minA && minB >= minC)
            norm = t3 <= t4 ? new Vec3(0, -1, 0) : new Vec3(0, 1, 0);
        else
            norm = t5 <= t6 ? new Vec3(0, 0, -1) : new Vec3(0, 0, 1);

        return new RayHit(t, inside ? -norm : norm);
    }

    public static AABox FromCenter(Vec3 center, Vec3 size) => new AABox(center - size * 0.5f, center + size * 0.5f);

    public static AABox Inverted() => new AABox(
        new Vec3(float.MaxValue, float.MaxValue, float.MaxValue),
        new Vec3(float.MinValue, float.MinValue, float.MinValue));

    public static AABox Infinite() => new AABox(
        new Vec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity),
        new Vec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity));
}

struct Box : IShape
{
    public AABox Local;
    public Quat Rot;

    public Box(AABox local, Quat rot)
    {
        Debug.Assert(rot.IsUnit, "Box rotation must be normalized");
        Local = local;
        Rot = rot;
    }

    public Vec3 Center => Local.Center;

    public Vec3 ClosestPoint(Vec3 p) => Center + Rot * (Local.ClosestPoint(LocalPoint(p)) - Center);

    public AABox Bounds()
    {
        // Rotate each half-extent axis and sum absolute contributions per world axis.
        Vec3 c = Local.Center;
        Vec3 h = Local.Size * 0.5f;
        Vec3 ax = Rot * new Vec3(h.X, 0, 0);
        Vec3 ay = Rot * new Vec3(0, h.Y, 0);
        Vec3 az = Rot * new Vec3(0, 0, h.Z);
        Vec3 extent = new Vec3(
            MathF.Abs(ax.X) + MathF.Abs(ay.X) + MathF.Abs(az.X),
            MathF.Abs(ax.Y) + MathF.Abs(ay.Y) + MathF.Abs(az.Y),
            MathF.Abs(ax.Z) + MathF.Abs(ay.Z) + MathF.Abs(az.Z));
        return AABox.FromCenter(c, extent * 2f);
    }

    public bool Overlaps(AABox box)
    {
        // Separating Axis Theorem for OBB vs AABB.
        Vec3 obbCenter = Local.Center;
        Vec3 obbHalf = Local.Size * 0.5f;
        Vec3 aabbCenter = box.Center;
        Vec3 aabbHalf = box.Size * 0.5f;
        Vec3 d = obbCenter - aabbCenter;

        Vec3 ax = Rot * new Vec3(1, 0, 0);
        Vec3 ay = Rot * new Vec3(0, 1, 0);
        Vec3 az = Rot * new Vec3(0, 0, 1);

        bool Separated(Vec3 axis)
        {
            if (axis.MagnitudeSqr() < 1e-6f) return false;
            float dist = MathF.Abs(Vec3.Dot(d, axis));
            float rA = aabbHalf.X * MathF.Abs(axis.X) + aabbHalf.Y * MathF.Abs(axis.Y) + aabbHalf.Z * MathF.Abs(axis.Z);
            float rB = obbHalf.X * MathF.Abs(Vec3.Dot(ax, axis)) + obbHalf.Y * MathF.Abs(Vec3.Dot(ay, axis)) + obbHalf.Z * MathF.Abs(Vec3.Dot(az, axis));
            return dist > rA + rB;
        }

        // 3 AABB axes.
        if (Separated(new Vec3(1, 0, 0))) return false;
        if (Separated(new Vec3(0, 1, 0))) return false;
        if (Separated(new Vec3(0, 0, 1))) return false;
        // 3 OBB axes.
        if (Separated(ax)) return false;
        if (Separated(ay)) return false;
        if (Separated(az)) return false;
        // 9 cross-product axes.
        if (Separated(Vec3.Cross(new Vec3(1, 0, 0), ax))) return false;
        if (Separated(Vec3.Cross(new Vec3(1, 0, 0), ay))) return false;
        if (Separated(Vec3.Cross(new Vec3(1, 0, 0), az))) return false;
        if (Separated(Vec3.Cross(new Vec3(0, 1, 0), ax))) return false;
        if (Separated(Vec3.Cross(new Vec3(0, 1, 0), ay))) return false;
        if (Separated(Vec3.Cross(new Vec3(0, 1, 0), az))) return false;
        if (Separated(Vec3.Cross(new Vec3(0, 0, 1), ax))) return false;
        if (Separated(Vec3.Cross(new Vec3(0, 0, 1), ay))) return false;
        if (Separated(Vec3.Cross(new Vec3(0, 0, 1), az))) return false;

        return true;
    }

    public RayHit? Intersect(Ray ray)
    {
        RayHit? hit = Local.Intersect(LocalRay(ray));
        if (hit is RayHit h)
            return new RayHit(h.Dist, Rot * h.Norm);
        return null;
    }

    private Vec3 LocalPoint(Vec3 p) => Center + Rot.Inverse() * (p - Center);
    private Ray LocalRay(Ray ray) => new Ray(LocalPoint(ray.Origin), Rot.Inverse() * ray.Dir);

    public static Box FromCenter(Vec3 center, Vec3 size, Quat rot) =>
        new Box(AABox.FromCenter(center, size), rot);
}

struct Sphere : IShape
{
    public Vec3 Center;
    public float Radius;

    public Sphere(Vec3 center, float radius)
    {
        Debug.Assert(radius > 0f, "Radius must be positive");
        Center = center;
        Radius = radius;
    }

    public bool Overlaps(Sphere other)
    {
        float distSqr = (other.Center - Center).MagnitudeSqr();
        float radSum = Radius + other.Radius;
        return distSqr <= radSum * radSum;
    }

    public AABox Bounds()
    {
        Vec3 r = new Vec3(Radius, Radius, Radius);
        return new AABox(Center - r, Center + r);
    }

    public bool Overlaps(AABox box)
    {
        Vec3 closest = box.ClosestPoint(Center);
        return (closest - Center).MagnitudeSqr() <= Radius * Radius;
    }

    public RayHit? Intersect(Ray ray)
    {
        // https://gdbooks.gitbooks.io/3dcollisions/content/Chapter3/raycast_sphere.html
        Vec3 toCenter = Center - ray.Origin;
        float toCenterDistSqr = toCenter.MagnitudeSqr();
        float a = Vec3.Dot(toCenter, ray.Dir);
        float discriminant = Radius * Radius - toCenterDistSqr + a * a;

        if (discriminant < 0f) return null;

        float f = MathF.Sqrt(discriminant);
        float t = toCenterDistSqr < Radius * Radius ? a + f : a - f;

        if (t < 0f) return null;

        Vec3 norm = (ray[t] - Center).Normalize();
        return new RayHit(t, norm);
    }
}

struct Capsule : IShape
{
    public Line Spine;
    public float Radius;

    public Capsule(Line spine, float radius)
    {
        Debug.Assert(radius > 0f, "Radius must be positive");
        Spine = spine;
        Radius = radius;
    }

    public bool Overlaps(Sphere sphere)
    {
        float distSqr = Spine.DistanceSqr(sphere.Center);
        float radiusSum = Radius + sphere.Radius;
        return distSqr <= radiusSum * radiusSum;
    }

    public AABox Bounds()
    {
        Vec3 r = new Vec3(Radius, Radius, Radius);
        return new AABox(
            new Vec3(MathF.Min(Spine.A.X, Spine.B.X), MathF.Min(Spine.A.Y, Spine.B.Y), MathF.Min(Spine.A.Z, Spine.B.Z)) - r,
            new Vec3(MathF.Max(Spine.A.X, Spine.B.X), MathF.Max(Spine.A.Y, Spine.B.Y), MathF.Max(Spine.A.Z, Spine.B.Z)) + r);
    }

    public bool Overlaps(AABox box)
    {
        // Ping-pong closest point iteration between spine and box.
        Vec3 spinePoint = Spine.ClosestPoint(box.Center);
        Vec3 boxPoint = box.ClosestPoint(spinePoint);
        spinePoint = Spine.ClosestPoint(boxPoint);
        boxPoint = box.ClosestPoint(spinePoint);
        return (spinePoint - boxPoint).MagnitudeSqr() <= Radius * Radius;
    }

    public RayHit? Intersect(Ray ray)
    {
        Vec3 spinePoint = Spine.ClosestPoint(ray);
        Sphere sphere = new Sphere(spinePoint, Radius);
        return sphere.Intersect(ray);
    }
}

struct Plane : IShape
{
    public Vec3 Normal;
    public float Distance;

    public Plane(Vec3 normal, float distance)
    {
        Debug.Assert(normal.IsUnit, "Plane normal must be normalized");
        Normal = normal;
        Distance = distance;
    }

    public Vec3 Position => Normal * Distance;

    public Vec3 ClosestPoint(Vec3 point) => point - Normal * (Vec3.Dot(Normal, point) - Distance);
    public AABox Bounds() => AABox.Infinite();

    public bool Overlaps(AABox box)
    {
        Vec3 center = box.Center;
        Vec3 half = box.Size * 0.5f;
        float c = Vec3.Dot(Normal, center);
        float r = half.X * MathF.Abs(Normal.X) + half.Y * MathF.Abs(Normal.Y) + half.Z * MathF.Abs(Normal.Z);
        return c - r <= Distance && Distance <= c + r;
    }

    public RayHit? Intersect(Ray ray)
    {
        float dirDot = Vec3.Dot(ray.Dir, Normal);
        if (dirDot >= 0f) return null; // Parallel or back-facing.
        float t = (Distance - Vec3.Dot(ray.Origin, Normal)) / dirDot;
        if (t < 0f) return null; // Plane behind ray origin.
        return new RayHit(t, Normal);
    }

    public static Plane AtPosition(Vec3 normal, Vec3 position) =>
        new Plane(normal, Vec3.Dot(normal, position));

    public static Plane AtTriangle(Vec3 a, Vec3 b, Vec3 c)
    {
        Vec3 normal = Vec3.Cross(b - a, c - a).Normalize();
        return new Plane(normal, Vec3.Dot(normal, a));
    }
}

struct Triangle : IShape
{
    public Vec3 A, B, C;

    public Triangle(Vec3 a, Vec3 b, Vec3 c)
    {
        A = a;
        B = b;
        C = c;
    }

    public Vec3 Normal => Vec3.Cross(B - A, C - A).Normalize();
    public Vec3 Center => (A + B + C) / 3f;
    public Plane Plane => Plane.AtTriangle(A, B, C);

    public AABox Bounds() => new AABox(
        new Vec3(MathF.Min(A.X, MathF.Min(B.X, C.X)), MathF.Min(A.Y, MathF.Min(B.Y, C.Y)), MathF.Min(A.Z, MathF.Min(B.Z, C.Z))),
        new Vec3(MathF.Max(A.X, MathF.Max(B.X, C.X)), MathF.Max(A.Y, MathF.Max(B.Y, C.Y)), MathF.Max(A.Z, MathF.Max(B.Z, C.Z))));

    public bool Overlaps(AABox box) => Bounds().Overlaps(box);

    public RayHit? Intersect(Ray ray)
    {
        // Möller–Trumbore intersection.
        // https://en.wikipedia.org/wiki/M%C3%B6ller%E2%80%93Trumbore_intersection_algorithm
        Vec3 ab = B - A;
        Vec3 ac = C - A;
        Vec3 h = Vec3.Cross(ray.Dir, ac);
        float det = Vec3.Dot(ab, h);

        if (det <= 1e-6f) return null; // Back-facing or parallel.

        float invDet = 1f / det;
        Vec3 ao = ray.Origin - A;
        float u = Vec3.Dot(ao, h) * invDet;
        if (u < 0f || u > 1f) return null;

        Vec3 q = Vec3.Cross(ao, ab);
        float v = Vec3.Dot(ray.Dir, q) * invDet;
        if (v < 0f || u + v > 1f) return null;

        float t = Vec3.Dot(ac, q) * invDet;
        if (t < 0f) return null;

        return new RayHit(t, Normal);
    }
}

struct View
{
    public Transform Trans;
    public float Fov; // Field of view in radians. Horizontal aspect >= 1, vertical when aspect < 1.
    public float Near;

    public View()
    {
        Trans = Transform.Identity();
        Fov = float.DegreesToRadians(60f);
        Near = 0.1f;
    }

    public View(Transform trans)
    {
        Debug.Assert(trans.Scale.IsOne, "View cannot be scaled");
        Trans = trans;
        Fov = float.DegreesToRadians(60f);
        Near = 0.1f;
    }

    public View(Transform trans, float fov, float near = 0.1f)
    {
        Debug.Assert(trans.Scale.IsOne, "View cannot be scaled");
        Debug.Assert(fov > 0f && fov < MathF.PI, "Invalid fov");
        Debug.Assert(near > 0f, "Invalid near plane");
        Trans = trans;
        Fov = fov;
        Near = near;
    }

    public Ray Ray(Vec2 screenPos, float aspect)
    {
        Debug.Assert(aspect > 0f, "Invalid aspect");

        float ndcX = screenPos.X * 2f - 1f;
        float ndcY = -screenPos.Y * 2f + 1f;

        float tanHalfHor, tanHalfVer;
        if (aspect >= 1f)
        {
            tanHalfHor = MathF.Tan(Fov * 0.5f);
            tanHalfVer = tanHalfHor / aspect;
        }
        else
        {
            tanHalfVer = MathF.Tan(Fov * 0.5f);
            tanHalfHor = tanHalfVer * aspect;
        }

        Vec3 localDir = new Vec3(ndcX * tanHalfHor, ndcY * tanHalfVer, 1f);
        Vec3 origin = Trans.Pos + Trans.TransformDir(new Vec3(0f, 0f, Near));
        Vec3 dir = Trans.TransformDir(localDir.Normalize());
        return new Ray(origin, dir);
    }

    public (Vec2 ScreenPos, float Depth)? Project(Vec3 worldPos, float aspect)
    {
        Vec3 local = Trans.TransformPointInv(worldPos);
        if (local.Z <= Near)
            return null;

        float tanHalfHor, tanHalfVer;
        if (aspect >= 1f)
        {
            tanHalfHor = MathF.Tan(Fov * 0.5f);
            tanHalfVer = tanHalfHor / aspect;
        }
        else
        {
            tanHalfVer = MathF.Tan(Fov * 0.5f);
            tanHalfHor = tanHalfVer * aspect;
        }

        // Project relative to the ray origin.
        Vec3 localFromNear = new Vec3(local.X, local.Y, local.Z - Near);
        float ndcX = (localFromNear.X / localFromNear.Z) / tanHalfHor;
        float ndcY = (localFromNear.Y / localFromNear.Z) / tanHalfVer;
        Vec2 screenPos = new Vec2((ndcX + 1f) * 0.5f, (1f - ndcY) * 0.5f);
        return (screenPos, localFromNear.Magnitude());
    }
}

struct Rng
{
    uint _s0, _s1, _s2, _s3, _s4;

    public Rng(uint seedA, uint seedB) : this((ulong)seedA << 32 | seedB) { }

    public Rng(ulong seed)
    {
        ulong val1 = SplitMix64(ref seed);
        ulong val2 = SplitMix64(ref seed);
        _s0 = (uint)val1;
        _s1 = (uint)(val1 >> 32);
        _s2 = (uint)val2;
        _s3 = (uint)(val2 >> 32);
        _s4 = 0;
    }

    public uint NextUInt()
    {
        // https://en.wikipedia.org/wiki/Xorshift#xorwow
        Debug.Assert(_s0 != 0 || _s1 != 0 || _s2 != 0 || _s3 != 0);

        uint t = _s3;
        uint s = _s0;
        _s3 = _s2;
        _s2 = _s1;
        _s1 = s;

        t ^= t >> 2;
        t ^= t << 1;
        t ^= s ^ (s << 4);

        _s0 = t;
        _s4 += 362437u;

        return t + _s4;
    }

    public T NextRange<T>(T min, T max) where T : INumber<T>
    {
        Debug.Assert(max > min);
        float range = float.CreateSaturating(max - min);
        return min + T.CreateTruncating(NextFloat() * range);
    }

    public float NextFloat() // 0.0 (inclusive) to 1.0 (exclusive).
    {
        const float toFloat = 1.0f / ((float)uint.MaxValue + 512f);
        return NextUInt() * toFloat;
    }

    public (float A, float B) NextGauss()
    {
        // Box-Muller transform: https://en.wikipedia.org/wiki/Box%E2%80%93Muller_transform
        float a;
        do { a = NextFloat(); } while (a <= 1e-8f);

        float b = MathF.PI * 2f * NextFloat();
        float mag = MathF.Sqrt(-2f * MathF.Log(a));
        return (mag * MathF.Cos(b), mag * MathF.Sin(b));
    }

    private static ulong SplitMix64(ref ulong state)
    {
        // Implementation of the 'splitmix' algorithm.
        // Source: https://en.wikipedia.org/wiki/Xorshift#xorwow
        ulong result = state += 0x9e3779b97f4a7c15UL;
        result = (result ^ (result >> 30)) * 0xbf58476d1ce4e5b9UL;
        result = (result ^ (result >> 27)) * 0x94d049bb133111ebUL;
        return result ^ (result >> 31);
    }
}

struct Timestamp
{
    private long _ticks;

    private Timestamp(long ticks) { _ticks = ticks; }

    public double Nanos => (double)_ticks * 1_000_000_000.0 / Stopwatch.Frequency;
    public double Micros => (double)_ticks * 1_000_000.0 / Stopwatch.Frequency;
    public double Millis => (double)_ticks * 1_000.0 / Stopwatch.Frequency;
    public double Seconds => (double)_ticks / Stopwatch.Frequency;

    public static Timestamp operator -(Timestamp a, Timestamp b) => new Timestamp(a._ticks - b._ticks);

    public static Timestamp Now() => new Timestamp(Stopwatch.GetTimestamp());
}
