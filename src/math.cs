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

    public Quat Inverse() => new Quat(-X, -Y, -Z, W);

    public Quat Normalize()
    {
        float mag = MathF.Sqrt(X * X + Y * Y + Z * Z + W * W);
        if (mag < 1e-6f) throw new InvalidOperationException("Cannot normalize a zero quaternion.");
        float magInv = 1.0f / mag;
        return new Quat(X * magInv, Y * magInv, Z * magInv, W * magInv);
    }
}

struct Mat4
{
    public Vec4 C0, C1, C2, C3; // Column-major.

    Mat4(Vec4 c0, Vec4 c1, Vec4 c2, Vec4 c3)
    {
        C0 = c0; C1 = c1; C2 = c2; C3 = c3;
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

    Vec4 Row(int i) => new Vec4(C0[i], C1[i], C2[i], C3[i]);

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
