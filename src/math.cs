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

struct Ray3
{
    Vec3 Origin, Dir;

    public Ray3(Vec3 origin, Vec3 dir)
    {
        Origin = origin;
        Dir = dir;
    }

    public Vec3 this[float t] => Origin + Dir * t;

    public float Distance(Vec3 p) => Vec3.Dot(p - Origin, Dir);
}
