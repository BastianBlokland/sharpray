using System;

struct Vec2
{
    public float X, Y;

    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float this[int i] => i switch { 0 => X, 1 => Y, _ => throw new IndexOutOfRangeException() };
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

    public float this[int i] => i switch { 0 => X, 1 => Y, 2 => Z, _ => throw new IndexOutOfRangeException() };

    public float MagnitudeSqr() => Dot(this, this);
    public float Magnitude() => MathF.Sqrt(MagnitudeSqr());

    public Vec3 Normalize()
    {
        float m = Magnitude();
        if (m < 1e-6f) throw new InvalidOperationException("Cannot normalize a zero vector.");
        return this / m;
    }

    public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, float b) => new Vec3(a.X * b, a.Y * b, a.Z * b);
    public static Vec3 operator *(float a, Vec3 b) => new Vec3(a * b.X, a * b.Y, a * b.Z);
    public static Vec3 operator /(Vec3 a, float b) => new Vec3(a.X / b, a.Y / b, a.Z / b);

    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static Vec3 Project(Vec3 v, Vec3 n)
    {
        float nSqrMag = n.MagnitudeSqr();
        if (nSqrMag < 1e-6f) throw new InvalidOperationException("Cannot project onto a zero vector.");
        return n * (Dot(v, n) / nSqrMag);
    }

    public static Vec3 Reflect(Vec3 v, Vec3 n) => v - 2 * Dot(v, n) * n;

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

    public float this[int i] => i switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new IndexOutOfRangeException() };

    public static Vec4 operator -(Vec4 v) => new Vec4(-v.X, -v.Y, -v.Z, -v.W);
    public static Vec4 operator +(Vec4 a, Vec4 b) => new Vec4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vec4 operator -(Vec4 a, Vec4 b) => new Vec4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vec4 operator *(Vec4 a, float b) => new Vec4(a.X * b, a.Y * b, a.Z * b, a.W * b);
    public static Vec4 operator *(float a, Vec4 b) => new Vec4(a * b.X, a * b.Y, a * b.Z, a * b.W);
    public static Vec4 operator /(Vec4 a, float b) => new Vec4(a.X / b, a.Y / b, a.Z / b, a.W / b);

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

    public float this[int i] => i switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new IndexOutOfRangeException() };

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
        Vec3 vec = axis * MathF.Sin(angle * 0.5f);
        return new Quat(vec.X, vec.Y, vec.Z, MathF.Cos(angle * 0.5f));
    }

    public static Quat FromTo(Quat from, Quat to) => to * from.Inverse();

    public static float Dot(Quat a, Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    public Quat Inverse() => new Quat(-X, -Y, -Z, W);

    public Quat Normalize()
    {
        float mag = MathF.Sqrt(X * X + Y * Y + Z * Z + W * W);
        if (mag < 1e-6f) throw new InvalidOperationException("Cannot normalize a zero quaternion.");
        float magInv = 1.0f / mag;
        return new Quat(X * magInv, Y * magInv, Z * magInv, W * magInv);
    }
}

struct Ray3
{
    public Vec3 Origin, Dir;

    public Ray3(Vec3 origin, Vec3 dir)
    {
        Origin = origin;
        Dir = dir;
    }

    public Vec3 this[float t] => Origin + Dir * t;

    public float Distance(Vec3 p) => Vec3.Dot(p - Origin, Dir);
}
