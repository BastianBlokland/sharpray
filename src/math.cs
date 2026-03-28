using System;
using System.Diagnostics;

struct Color
{
    public float R, G, B;

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

    public Vec3 Normalize()
    {
        float m = Magnitude();
        Debug.Assert(m >= 1e-6f, "Cannot normalize a zero vector");
        return this / m;
    }

    public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, float b) => new Vec3(a.X * b, a.Y * b, a.Z * b);
    public static Vec3 operator *(float a, Vec3 b) => new Vec3(a * b.X, a * b.Y, a * b.Z);
    public static Vec3 operator /(Vec3 a, float b)
    {
        Debug.Assert(b != 0f);
        return new Vec3(a.X / b, a.Y / b, a.Z / b);
    }

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
        return v - 2 * (Dot(v, n) / Dot(n, n)) * n;
    }

    public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => new Vec3(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t);
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

    public Vec3 TransformPoint(Vec3 p) => Pos + Rot * new Vec3(p.X * Scale.X, p.Y * Scale.Y, p.Z * Scale.Z);
    public Vec3 TransformPointInv(Vec3 p)
    {
        Vec3 r = Rot.Inverse() * (p - Pos);
        return new Vec3(r.X / Scale.X, r.Y / Scale.Y, r.Z / Scale.Z);
    }

    public Transform Inverse()
    {
        Vec3 invScale = new Vec3(1f / Scale.X, 1f / Scale.Y, 1f / Scale.Z);
        Quat invRot = Rot.Inverse();
        Vec3 invPos = invRot * new Vec3(-Pos.X * invScale.X, -Pos.Y * invScale.Y, -Pos.Z * invScale.Z);
        return new Transform(invPos, invRot, invScale);
    }

    public Mat4 ToMat() => Mat4.Trs(Pos, Rot, Scale);

    public static Transform Identity() => new Transform(new Vec3(0, 0, 0), Quat.Identity(), new Vec3(1, 1, 1));

    public static Transform operator *(Transform a, Transform b) => new Transform(
        a.TransformPoint(b.Pos),
        a.Rot * b.Rot,
        new Vec3(a.Scale.X * b.Scale.X, a.Scale.Y * b.Scale.Y, a.Scale.Z * b.Scale.Z));
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

struct AABox
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

    public AABox Encapsulate(Vec3 p) => new AABox(
        new Vec3(MathF.Min(Min.X, p.X), MathF.Min(Min.Y, p.Y), MathF.Min(Min.Z, p.Z)),
        new Vec3(MathF.Max(Max.X, p.X), MathF.Max(Max.Y, p.Y), MathF.Max(Max.Z, p.Z)));

    public AABox Encapsulate(AABox other) => new AABox(
        new Vec3(MathF.Min(Min.X, other.Min.X), MathF.Min(Min.Y, other.Min.Y), MathF.Min(Min.Z, other.Min.Z)),
        new Vec3(MathF.Max(Max.X, other.Max.X), MathF.Max(Max.Y, other.Max.Y), MathF.Max(Max.Z, other.Max.Z)));

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
}

struct Box
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

struct Sphere
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

struct Capsule
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

    public RayHit? Intersect(Ray ray)
    {
        Vec3 spinePoint = Spine.ClosestPoint(ray);
        Sphere sphere = new Sphere(spinePoint, Radius);
        return sphere.Intersect(ray);
    }
}

struct Plane
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

struct Triangle
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

    public RayHit? Intersect(Ray ray)
    {
        // Möller–Trumbore intersection.
        // https://en.wikipedia.org/wiki/M%C3%B6ller%E2%80%93Trumbore_intersection_algorithm
        Vec3 ab = B - A;
        Vec3 ac = C - A;
        Vec3 h = Vec3.Cross(ray.Dir, ac);
        float det = Vec3.Dot(ab, h);

        if (det >= -1e-6f) return null; // Back-facing or parallel.

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
        Trans = trans;
        Fov = float.DegreesToRadians(60f);
        Near = 0.1f;
    }

    public View(Transform trans, float fov, float near)
    {
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
}
