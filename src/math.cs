using System;
using System.Diagnostics;
using System.Numerics;

interface IShapeHit
{
    float Dist { get; }
}

interface IShape<THit> where THit : struct, IShapeHit
{
    AABox Bounds();
    bool Overlaps(AABox box);
    THit? Intersect(Ray ray);
    bool IntersectAny(Ray ray) => Intersect(ray) is not null;
}

interface IShape : IShape<ShapeHit> { }
interface IShapeLean : IShape<ShapeHitLean> { }

struct ShapeHit : IShapeHit
{
    public float Dist { get; }
    public Vec3 Norm;
    public Vec4 Tan; // xyz = tangent direction, w = bitangent handedness (+1/-1).
    public Vec2 Uv;

    public ShapeHit(float dist, Vec3 norm, Vec4 tan, Vec2 uv)
    {
        Debug.Assert(norm.IsUnit, "ShapeHit normal must be normalized");
        Debug.Assert(tan.Xyz.IsUnit, "ShapeHit tangent must be normalized");
        Dist = dist;
        Norm = norm;
        Tan = tan;
        Uv = uv;
    }
}

struct ShapeHitLean : IShapeHit
{
    public float Dist { get; }
    public Vec2 Uv;

    public ShapeHitLean(float dist, Vec2 uv)
    {
        Dist = dist;
        Uv = uv;
    }
}

static class ShapeExtensions
{
    public static ShapeHit? Intersect(this IShape shape, Ray ray, Transform trans)
    {
        var (localRay, localRayScale) = trans.TransformRayInv(ray);
        if (shape.Intersect(localRay) is ShapeHit hit)
        {
            Vec3 worldNorm = (trans.Rot * (hit.Norm / trans.Scale)).Normalize();
            Vec3 worldTan = (trans.Rot * hit.Tan.Xyz).Normalize();
            Vec4 hitTan = new Vec4(worldTan, hit.Tan.W);
            return new ShapeHit(hit.Dist / localRayScale, worldNorm, hitTan, hit.Uv);
        }
        return null;
    }

    public static bool IntersectAny(this IShape shape, Ray ray, Transform trans)
    {
        var (localRay, _) = trans.TransformRayInv(ray);
        return shape.IntersectAny(localRay);
    }
}

struct Color : ISpanFormattable
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

    public float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;
    public bool IsFinite => float.IsFinite(R) && float.IsFinite(G) && float.IsFinite(B);

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

    public override string ToString() => FormatUtils.FormatSet(stackalloc float[] { R, G, B }, "G3");
    public string ToString(string? format, IFormatProvider? provider) => ToString();
    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
        => FormatUtils.FormatSet(dest, out written, stackalloc float[] { R, G, B }, format.IsEmpty ? "G3" : format);

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

    public static Color FromPixelLinear(Pixel pixel) =>
        new Color(pixel.R / 255f, pixel.G / 255f, pixel.B / 255f);

    public static Color ForIndex(int index)
    {
        const float goldenRatioConj = 0.618033988f;
        float hue = (index * goldenRatioConj) % 1f;
        return FromHsv(hue, 0.8f, 0.95f);
    }

    public static Color FromHsv(float h, float s, float v)
    {
        Debug.Assert(h >= 0f && h <= 1f);
        Debug.Assert(s >= 0f && s <= 1f);
        Debug.Assert(v >= 0f && v <= 1f);

        if (v == 0f) return Black;
        if (s == 0f) return new Color(v);

        // hsv to rgb.
        // http://ilab.usc.edu/wiki/index.php/HSV_And_H2SV_Color_Space#HSV_Transformation_C_.2F_C.2B.2B_Code_2

        float hueSeg = h * 6f;
        int hueIdx = (int)MathF.Floor(hueSeg);
        float hueFrac = hueSeg - hueIdx;
        float pV = v * (1f - s);
        float qV = v * (1f - s * hueFrac);
        float tV = v * (1f - s * (1f - hueFrac));
        return hueIdx switch
        {
            0 => new Color(v, tV, pV),
            1 => new Color(qV, v, pV),
            2 => new Color(pV, v, tV),
            3 => new Color(pV, qV, v),
            4 => new Color(tV, pV, v),
            _ => new Color(v, pV, qV),
        };
    }
}

struct Vec2 : ISpanFormattable
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

    public override string ToString() => FormatUtils.FormatSet(stackalloc float[] { X, Y }, "G3");
    public string ToString(string? format, IFormatProvider? provider) => ToString();
    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
        => FormatUtils.FormatSet(dest, out written, stackalloc float[] { X, Y }, format.IsEmpty ? "G3" : format);

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
    public static Vec2 One => new Vec2(1f, 1f);
}

struct Vec2i : ISpanFormattable
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

    public override string ToString() => FormatUtils.FormatSet(stackalloc int[] { X, Y });
    public string ToString(string? format, IFormatProvider? provider) => ToString();
    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
        => FormatUtils.FormatSet(dest, out written, stackalloc int[] { X, Y }, format);

    public static Vec2i operator -(Vec2i v) => new Vec2i(-v.X, -v.Y);
    public static Vec2i operator +(Vec2i a, Vec2i b) => new Vec2i(a.X + b.X, a.Y + b.Y);
    public static Vec2i operator -(Vec2i a, Vec2i b) => new Vec2i(a.X - b.X, a.Y - b.Y);
    public static Vec2i operator *(Vec2i v, int s) => new Vec2i(v.X * s, v.Y * s);
    public static Vec2i operator *(int s, Vec2i v) => new Vec2i(s * v.X, s * v.Y);

    public static Vec2i Zero => new Vec2i(0, 0);
}

struct Vec3 : ISpanFormattable
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

    public Vec3 Perp()
    {
        Debug.Assert(IsUnit, "Perp requires a unit vector");
        Vec3 up = MathF.Abs(Y) < 0.9f ? Up : Right;
        return Cross(up, this).Normalize();
    }

    public override string ToString() => FormatUtils.FormatSet(stackalloc float[] { X, Y, Z }, "G3");
    public string ToString(string? format, IFormatProvider? provider) => ToString();
    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
        => FormatUtils.FormatSet(dest, out written, stackalloc float[] { X, Y, Z }, format.IsEmpty ? "G3" : format);

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

struct Vec4 : ISpanFormattable
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

    public Vec3 Xyz => new Vec3(X, Y, Z);

    public override string ToString() => FormatUtils.FormatSet(stackalloc float[] { X, Y, Z, W }, "G3");
    public string ToString(string? format, IFormatProvider? provider) => ToString();
    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
        => FormatUtils.FormatSet(dest, out written, stackalloc float[] { X, Y, Z, W }, format.IsEmpty ? "G3" : format);

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

struct Quat : ISpanFormattable
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

    public override string ToString() => FormatUtils.FormatSet(stackalloc float[] { X, Y, Z, W }, "G3");
    public string ToString(string? format, IFormatProvider? provider) => ToString();
    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
        => FormatUtils.FormatSet(dest, out written, stackalloc float[] { X, Y, Z, W }, format.IsEmpty ? "G3" : format);

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
    public Vec3 Origin, Dir, DirInv;

    public Ray(Vec3 origin, Vec3 dir)
    {
        Debug.Assert(dir.IsUnit, "Ray direction must be normalized");
        Origin = origin;
        Dir = dir;

        const float minComp = 1e-8f; // Minimal component value to avoid NaNs.
        DirInv = new Vec3(
            1f / (MathF.Abs(dir.X) >= minComp ? dir.X : MathF.CopySign(minComp, dir.X)),
            1f / (MathF.Abs(dir.Y) >= minComp ? dir.Y : MathF.CopySign(minComp, dir.Y)),
            1f / (MathF.Abs(dir.Z) >= minComp ? dir.Z : MathF.CopySign(minComp, dir.Z)));
    }

    public Vec3 this[float t] => Origin + Dir * t;

    public float Distance(Vec3 p) => Vec3.Dot(p - Origin, Dir);
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
    public float SurfaceArea => 2f * (Size.X * Size.Y + Size.X * Size.Z + Size.Y * Size.Z);
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

    public float? IntersectDist(Ray ray)
    {
        // Cyrus-Beck slab clipping, returns entry distance (0 if ray starts inside).
        // https://izzofinal.wordpress.com/2012/11/09/ray-vs-box-round-1/
        float t1 = (Min.X - ray.Origin.X) * ray.DirInv.X;
        float t2 = (Max.X - ray.Origin.X) * ray.DirInv.X;
        float t3 = (Min.Y - ray.Origin.Y) * ray.DirInv.Y;
        float t4 = (Max.Y - ray.Origin.Y) * ray.DirInv.Y;
        float t5 = (Min.Z - ray.Origin.Z) * ray.DirInv.Z;
        float t6 = (Max.Z - ray.Origin.Z) * ray.DirInv.Z;

        float minA = MathF.Min(t1, t2), minB = MathF.Min(t3, t4), minC = MathF.Min(t5, t6);
        float maxA = MathF.Max(t1, t2), maxB = MathF.Max(t3, t4), maxC = MathF.Max(t5, t6);

        float tMin = MathF.Max(MathF.Max(minA, minB), minC);
        float tMax = MathF.Min(MathF.Min(maxA, maxB), maxC);

        if (tMax < 0f || tMin > tMax)
            return null;

        return MathF.Max(0f, tMin); // 0 if ray starts inside.
    }

    public ShapeHit? Intersect(Ray ray)
    {
        // Cyrus-Beck slab clipping.
        // https://izzofinal.wordpress.com/2012/11/09/ray-vs-box-round-1/
        float t1 = (Min.X - ray.Origin.X) * ray.DirInv.X;
        float t2 = (Max.X - ray.Origin.X) * ray.DirInv.X;
        float t3 = (Min.Y - ray.Origin.Y) * ray.DirInv.Y;
        float t4 = (Max.Y - ray.Origin.Y) * ray.DirInv.Y;
        float t5 = (Min.Z - ray.Origin.Z) * ray.DirInv.Z;
        float t6 = (Max.Z - ray.Origin.Z) * ray.DirInv.Z;

        float minA = MathF.Min(t1, t2), minB = MathF.Min(t3, t4), minC = MathF.Min(t5, t6);
        float maxA = MathF.Max(t1, t2), maxB = MathF.Max(t3, t4), maxC = MathF.Max(t5, t6);

        float tMin = MathF.Max(MathF.Max(minA, minB), minC);
        float tMax = MathF.Min(MathF.Min(maxA, maxB), maxC);

        if (tMax < 0f || tMin > tMax)
            return null;

        bool inside = tMin < 0f;
        float t = inside ? tMax : tMin;
        Vec3 hit = ray[t];

        Vec3 norm;
        Vec4 tan;
        Vec2 face;
        if (minA >= minB && minA >= minC)
        {
            // X face: U=Y, V=Z, T=(0,1,0).
            bool negX = t1 <= t2;
            norm = negX ? new Vec3(-1, 0, 0) : new Vec3(1, 0, 0);
            tan = new Vec4(0, 1, 0, negX ? -1f : 1f);
            face = new Vec2((hit.Y - Min.Y) / (Max.Y - Min.Y), (hit.Z - Min.Z) / (Max.Z - Min.Z));
        }
        else if (minB >= minA && minB >= minC)
        {
            // Y face: U=X, V=Z, T=(1,0,0).
            bool negY = t3 <= t4;
            norm = negY ? new Vec3(0, -1, 0) : new Vec3(0, 1, 0);
            tan = new Vec4(1, 0, 0, negY ? 1f : -1f);
            face = new Vec2((hit.X - Min.X) / (Max.X - Min.X), (hit.Z - Min.Z) / (Max.Z - Min.Z));
        }
        else
        {
            // Z face: U=X, V=Y, T=(1,0,0).
            bool negZ = t5 <= t6;
            norm = negZ ? new Vec3(0, 0, -1) : new Vec3(0, 0, 1);
            tan = new Vec4(1, 0, 0, negZ ? -1f : 1f);
            face = new Vec2((hit.X - Min.X) / (Max.X - Min.X), (hit.Y - Min.Y) / (Max.Y - Min.Y));
        }

        return new ShapeHit(t, inside ? -norm : norm, tan, face);
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

    public void Corners(Span<Vec3> corners)
    {
        Debug.Assert(corners.Length >= 8, "Span must hold at least 8 corners");
        Vec3 halfSize = Local.Size * 0.5f;
        for (int i = 0; i != 8; ++i)
        {
            corners[i] = Local.Center + Rot * new Vec3(
                (i & 1) != 0 ? halfSize.X : -halfSize.X,
                (i & 2) != 0 ? halfSize.Y : -halfSize.Y,
                (i & 4) != 0 ? halfSize.Z : -halfSize.Z);
        }
    }

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

    public ShapeHit? Intersect(Ray ray)
    {
        ShapeHit? hit = Local.Intersect(LocalRay(ray));
        if (hit is ShapeHit h)
        {
            Vec3 norm = Rot * h.Norm;
            Vec4 tan = new Vec4(Rot * h.Tan.Xyz, h.Tan.W);
            return new ShapeHit(h.Dist, norm, tan, h.Uv);
        }
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

    public ShapeHit? Intersect(Ray ray)
    {
        // https://gdbooks.gitbooks.io/3dcollisions/content/Chapter3/raycast_sphere.html
        Vec3 toCenter = Center - ray.Origin;
        float toCenterDistSqr = toCenter.MagnitudeSqr();
        float a = Vec3.Dot(toCenter, ray.Dir);
        float discriminant = Radius * Radius - toCenterDistSqr + a * a;

        if (discriminant < 0f)
            return null;

        float f = MathF.Sqrt(discriminant);
        float t = toCenterDistSqr < Radius * Radius ? a + f : a - f;

        if (t < 0f)
            return null;

        Vec3 norm = (ray[t] - Center).Normalize();
        Vec4 tan = new Vec4(new Vec3(-norm.Z, 0f, norm.X).NormalizeOr(Vec3.Right), 1f);

        float texU = 0.5f + MathF.Atan2(norm.Z, norm.X) / (2f * MathF.PI);
        float texV = 0.5f - MathF.Asin(Math.Clamp(norm.Y, -1f, 1f)) / MathF.PI;

        return new ShapeHit(t, norm, tan, new Vec2(texU, texV));
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

    public ShapeHit? Intersect(Ray ray)
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

    public ShapeHit? Intersect(Ray ray)
    {
        float dirDot = Vec3.Dot(ray.Dir, Normal);
        if (dirDot >= 0f)
            return null; // Parallel or back-facing.
        float t = (Distance - Vec3.Dot(ray.Origin, Normal)) / dirDot;
        if (t < 0f)
            return null; // Plane behind ray origin.

        // Compute texture-coords based on the tangent frame.
        Vec3 tangentU = Normal.Perp();
        Vec3 tangentV = Vec3.Cross(Normal, tangentU);
        Vec3 hit = ray[t];
        Vec2 uv = new Vec2(Vec3.Dot(hit, tangentU), Vec3.Dot(hit, tangentV));

        return new ShapeHit(t, Normal, new Vec4(tangentU, 1f), uv);
    }

    public static Plane AtPosition(Vec3 normal, Vec3 position) =>
        new Plane(normal, Vec3.Dot(normal, position));

    public static Plane AtTriangle(Vec3 a, Vec3 b, Vec3 c)
    {
        Vec3 normal = Vec3.Cross(b - a, c - a).Normalize();
        return new Plane(normal, Vec3.Dot(normal, a));
    }
}

struct TriangleLean : IShapeLean
{
    public Vec3 PosA, PosAToB, PosAToC;

    public TriangleLean(Vec3 posA, Vec3 posB, Vec3 posC)
    {
        PosA = posA;
        PosAToB = posB - posA;
        PosAToC = posC - posA;
    }

    public Vec3 PosB => PosA + PosAToB;
    public Vec3 PosC => PosA + PosAToC;
    public Vec3 Normal => Vec3.Cross(PosAToB, PosAToC).Normalize();
    public Vec3 Center => (PosA + PosB + PosC) / 3f;

    public AABox Bounds()
    {
        Vec3 posB = PosB, posC = PosC;
        return new AABox(
            new Vec3(MathF.Min(PosA.X, MathF.Min(posB.X, posC.X)), MathF.Min(PosA.Y, MathF.Min(posB.Y, posC.Y)), MathF.Min(PosA.Z, MathF.Min(posB.Z, posC.Z))),
            new Vec3(MathF.Max(PosA.X, MathF.Max(posB.X, posC.X)), MathF.Max(PosA.Y, MathF.Max(posB.Y, posC.Y)), MathF.Max(PosA.Z, MathF.Max(posB.Z, posC.Z))));
    }

    public bool Overlaps(AABox box)
    {
        // Fast rejection: AABB vs AABB.
        if (!Bounds().Overlaps(box))
            return false;

        // Separating Axis Theorem: triangle vs AABB.
        // https://fileadmin.cs.lth.se/cs/Personal/Tomas_Akenine-Moller/code/tribox3.txt
        Vec3 h = box.Size * 0.5f;

        // Translate triangle vertices into box-centered space.
        Vec3 v0 = PosA - box.Center;
        Vec3 v1 = v0 + PosAToB;
        Vec3 v2 = v0 + PosAToC;
        Vec3 e2 = PosAToC - PosAToB; // Edge C→B in local space (PosC - PosB).

        // Tests one of the 9 edge cross-product axes.
        bool Separated(Vec3 axis)
        {
            float p0 = Vec3.Dot(v0, axis);
            float p1 = Vec3.Dot(v1, axis);
            float p2 = Vec3.Dot(v2, axis);
            float r = h.X * MathF.Abs(axis.X) + h.Y * MathF.Abs(axis.Y) + h.Z * MathF.Abs(axis.Z);
            return MathF.Max(MathF.Max(p0, p1), p2) < -r || MathF.Min(MathF.Min(p0, p1), p2) > r;
        }

        // 9 axes: each triangle edge crossed with each AABB edge (X, Y, Z).
        if (Separated(new Vec3(0f, -PosAToB.Z, PosAToB.Y))) return false;
        if (Separated(new Vec3(PosAToB.Z, 0f, -PosAToB.X))) return false;
        if (Separated(new Vec3(-PosAToB.Y, PosAToB.X, 0f))) return false;
        if (Separated(new Vec3(0f, -PosAToC.Z, PosAToC.Y))) return false;
        if (Separated(new Vec3(PosAToC.Z, 0f, -PosAToC.X))) return false;
        if (Separated(new Vec3(-PosAToC.Y, PosAToC.X, 0f))) return false;
        if (Separated(new Vec3(0f, -e2.Z, e2.Y))) return false;
        if (Separated(new Vec3(e2.Z, 0f, -e2.X))) return false;
        if (Separated(new Vec3(-e2.Y, e2.X, 0f))) return false;

        // Triangle face normal axis.
        Vec3 normal = Vec3.Cross(PosAToB, PosAToC);
        float d = Vec3.Dot(normal, v0);
        float rn = h.X * MathF.Abs(normal.X) + h.Y * MathF.Abs(normal.Y) + h.Z * MathF.Abs(normal.Z);
        if (d > rn || d < -rn)
            return false;

        return true;
    }

    public ShapeHitLean? Intersect(Ray ray)
    {
        // Möller–Trumbore intersection.
        // https://en.wikipedia.org/wiki/M%C3%B6ller%E2%80%93Trumbore_intersection_algorithm
        Vec3 h = Vec3.Cross(ray.Dir, PosAToC);
        float det = Vec3.Dot(PosAToB, h);

        if (det <= 1e-7f)
            return null; // Parallel or backface.

        const float edgeEps = 1e-7f;

        Vec3 ao = ray.Origin - PosA;
        float u = Vec3.Dot(ao, h);
        if (u < -edgeEps || u > det + edgeEps)
            return null;

        Vec3 q = Vec3.Cross(ao, PosAToB);
        float v = Vec3.Dot(ray.Dir, q);
        if (v < -edgeEps || u + v > det + edgeEps)
            return null;

        float tScaled = Vec3.Dot(PosAToC, q);
        if (tScaled < 0f)
            return null;

        float invDet = 1f / det;
        return new ShapeHitLean(tScaled * invDet, new Vec2(u * invDet, v * invDet));
    }

    public ShapeHit Inflate(
        ShapeHitLean leanHit,
        Vec3 normA, Vec3 normB, Vec3 normC,
        Vec4 tanA, Vec4 tanB, Vec4 tanC,
        Vec2 uvA, Vec2 uvB, Vec2 uvC)
    {
        float u = leanHit.Uv.X;
        float v = leanHit.Uv.Y;
        float w = 1f - u - v;

        Vec3 normInterp = normA * w + normB * u + normC * v;
        Vec3 norm = normInterp.MagnitudeSqr() >= 1e-12f ? normInterp.Normalize() : Normal;

        // Interpolate tangent and re-orthogonalize against interpolated normal.
        Vec4 tanInterp = tanA * w + tanB * u + tanC * v;
        Vec3 tanXyz = (tanInterp.Xyz - Vec3.Dot(tanInterp.Xyz, norm) * norm).NormalizeOr(Vec3.Right);
        Vec4 tan = new Vec4(tanXyz, tanInterp.W >= 0f ? 1f : -1f);

        Vec2 uv = uvA * w + uvB * u + uvC * v;

        return new ShapeHit(leanHit.Dist, norm, tan, uv);
    }

    public bool IntersectAny(Ray ray)
    {
        Vec3 h = Vec3.Cross(ray.Dir, PosAToC);
        float det = Vec3.Dot(PosAToB, h);

        bool backface = det < 0f;
        if (backface)
        {
            h = -h;
            det = -det;
        }
        if (det <= 1e-7f)
            return false; // Parallel.

        const float edgeEps = 1e-6f;

        Vec3 ao = ray.Origin - PosA;
        float u = Vec3.Dot(ao, h);
        if (u < -edgeEps || u > det + edgeEps)
            return false;

        Vec3 q = backface ? Vec3.Cross(PosAToB, ao) : Vec3.Cross(ao, PosAToB);
        float v = Vec3.Dot(ray.Dir, q);
        if (v < -edgeEps || u + v > det + edgeEps)
            return false;

        return Vec3.Dot(PosAToC, q) >= 0f;
    }
}

struct Triangle : IShape
{
    public TriangleLean Lean;
    public Vec3 NormA, NormB, NormC;
    public Vec4 TanA, TanB, TanC;
    public Vec2 UvA, UvB, UvC;

    public Triangle(Vec3 posA, Vec3 posB, Vec3 posC)
        : this(posA, posB, posC, Vec2.Zero, new Vec2(1, 0), new Vec2(0, 1)) { }

    public Triangle(
        Vec3 posA, Vec3 posB, Vec3 posC,
        Vec3 normA, Vec3 normB, Vec3 normC)
        : this(posA, posB, posC, normA, normB, normC, Vec2.Zero, new Vec2(1, 0), new Vec2(0, 1)) { }

    public Triangle(
        Vec3 posA, Vec3 posB, Vec3 posC,
        Vec2 uvA, Vec2 uvB, Vec2 uvC)
    {
        Lean = new TriangleLean(posA, posB, posC);
        NormA = NormB = NormC = Lean.Normal;
        TanA = TanB = TanC = ComputeTangent(posA, posB, posC, uvA, uvB, uvC, Lean.Normal);
        UvA = uvA; UvB = uvB; UvC = uvC;
    }

    public Triangle(
        Vec3 posA, Vec3 posB, Vec3 posC,
        Vec3 normA, Vec3 normB, Vec3 normC,
        Vec2 uvA, Vec2 uvB, Vec2 uvC)
    {
        Lean = new TriangleLean(posA, posB, posC);
        NormA = normA; NormB = normB; NormC = normC;
        UvA = uvA; UvB = uvB; UvC = uvC;

        Vec3 avgNorm = ((normA + normB + normC) / 3f).NormalizeOr(Lean.Normal);
        TanA = TanB = TanC = ComputeTangent(posA, posB, posC, uvA, uvB, uvC, avgNorm);
    }

    public Vec3 PosA => Lean.PosA;
    public Vec3 PosB => Lean.PosB;
    public Vec3 PosC => Lean.PosC;
    public Vec3 Normal => Lean.Normal;
    public Vec3 Center => Lean.Center;
    public Plane Plane => Plane.AtTriangle(PosA, PosB, PosC);

    public AABox Bounds() => Lean.Bounds();
    public bool Overlaps(AABox box) => Lean.Overlaps(box);

    public ShapeHit Inflate(ShapeHitLean leanHit) =>
        Lean.Inflate(leanHit, NormA, NormB, NormC, TanA, TanB, TanC, UvA, UvB, UvC);

    public ShapeHit? Intersect(Ray ray)
    {
        if (Lean.Intersect(ray) is not ShapeHitLean leanHit)
            return null;
        return Inflate(leanHit);
    }

    public bool IntersectAny(Ray ray) => Lean.IntersectAny(ray);

    public static Vec4 ComputeTangent(
        Vec3 posA, Vec3 posB, Vec3 posC,
        Vec2 uvA, Vec2 uvB, Vec2 uvC,
        Vec3 norm)
    {
        Vec3 posDelta1 = posB - posA;
        Vec3 posDelta2 = posC - posA;
        Vec2 uvDelta1 = uvB - uvA;
        Vec2 uvDelta2 = uvC - uvA;
        float f = uvDelta1.X * uvDelta2.Y - uvDelta2.X * uvDelta1.Y;

        Vec3 tangent;
        Vec3 bitangent;
        if (MathF.Abs(f) < 1e-8f)
        {
            tangent = (posDelta1 - Vec3.Dot(posDelta1, norm) * norm).NormalizeOr(Vec3.Right);
            return new Vec4(tangent, 1f);
        }

        float invF = 1f / f;
        tangent = (uvDelta2.Y * posDelta1 - uvDelta1.Y * posDelta2) * invF;
        bitangent = (-uvDelta2.X * posDelta1 + uvDelta1.X * posDelta2) * invF;

        tangent = (tangent - Vec3.Dot(tangent, norm) * norm).NormalizeOr(Vec3.Right);
        float w = Vec3.Dot(Vec3.Cross(norm, tangent), bitangent) < 0f ? -1f : 1f;
        return new Vec4(tangent, w);
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

struct Timestamp : ISpanFormattable
{
    private long _ticks;

    private Timestamp(long ticks) { _ticks = ticks; }

    public double Nanos => (double)_ticks * 1_000_000_000.0 / Stopwatch.Frequency;
    public double Micros => (double)_ticks * 1_000_000.0 / Stopwatch.Frequency;
    public double Millis => (double)_ticks * 1_000.0 / Stopwatch.Frequency;
    public double Seconds => (double)_ticks / Stopwatch.Frequency;

    public override string ToString() => FormatUtils.FormatTime(Micros);
    public string ToString(string? format, IFormatProvider? provider) => ToString();
    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
        => FormatUtils.FormatTime(dest, out written, Micros);

    public static Timestamp operator -(Timestamp a, Timestamp b) => new Timestamp(a._ticks - b._ticks);
    public static Timestamp operator *(Timestamp t, long n) => new Timestamp(t._ticks * n);
    public static Timestamp operator /(Timestamp t, long n) => new Timestamp(t._ticks / n);

    public static Timestamp FromNanos(long nanos) => new Timestamp(nanos * Stopwatch.Frequency / 1_000_000_000);
    public static Timestamp FromMicros(long micros) => new Timestamp(micros * Stopwatch.Frequency / 1_000_000);
    public static Timestamp Now() => new Timestamp(Stopwatch.GetTimestamp());
}
