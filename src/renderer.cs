using System;
using System.Diagnostics;
using System.Threading;

class Renderer
{
    public uint Width { get; }
    public uint Height { get; }
    public Color[] Radiance { get; }
    public Vec3[] Normals { get; }
    public float[] Depth { get; }

    private Scene _scene;
    private View _view;
    private float _aspect;

    private uint _blockSize;
    private uint _blockCountX, _blockCountY, _blockCountTotal;

    private int _blockNext;
    private volatile int _blockCompleted;
    private SemaphoreSlim _blockSignal;

    private uint _samples;
    private uint _bounces;

    public Renderer(
        Scene scene,
        View view,
        uint width,
        uint height,
        uint blockSize,
        uint samples,
        uint bounces)
    {
        Debug.Assert(width > 0 && height > 0);
        Debug.Assert(blockSize > 0);
        Debug.Assert(samples > 0);

        Width = width;
        Height = height;

        _scene = scene;
        _view = view;
        _aspect = (float)width / (float)height;

        _blockSize = blockSize;
        _blockCountX = (width + blockSize - 1) / blockSize;
        _blockCountY = (height + blockSize - 1) / blockSize;
        _blockCountTotal = _blockCountX * _blockCountY;
        _blockSignal = new SemaphoreSlim(0);

        _samples = samples;
        _bounces = bounces;

        scene.Lock();

        Radiance = new Color[Width * Height];
        Normals = new Vec3[Width * Height];
        Depth = new float[Width * Height];

        // Start the worker threads.
        for (int i = 0; i < Environment.ProcessorCount; ++i)
        {
            Thread thread = new Thread(Worker)
            {
                IsBackground = true
            };
            thread.Start();
        }
    }

    public (uint Step, uint Total) Tick()
    {
        if (_blockCompleted >= _blockCountTotal)
            return (_blockCountTotal, _blockCountTotal);

        _blockSignal.Wait();
        return ((uint)_blockCompleted, _blockCountTotal);
    }

    private void Worker()
    {
        while (true)
        {
            int block = Interlocked.Increment(ref _blockNext) - 1;
            if ((uint)block >= _blockCountTotal)
                break;

            Execute((uint)block);

            Interlocked.Increment(ref _blockCompleted);
            _blockSignal.Release();
        }
    }

    private void Execute(uint block)
    {
        Debug.Assert(block < _blockCountTotal);

        uint blockX = block % _blockCountX;
        uint blockY = block / _blockCountX;
        uint xMin = blockX * _blockSize;
        uint yMin = blockY * _blockSize;
        uint xMax = Math.Min(xMin + _blockSize, Width);
        uint yMax = Math.Min(yMin + _blockSize, Height);

        for (uint y = yMin; y != yMax; ++y)
        {
            for (uint x = xMin; x != xMax; ++x)
            {
                Fragment frag = Render(x, y);

                Radiance[y * Width + x] = frag.Radiance;
                Normals[y * Width + x] = frag.Normal ?? Vec3.Zero;
                Depth[y * Width + x] = frag.Depth ?? float.PositiveInfinity;
            }
        }
    }

    private Fragment Render(uint x, uint y)
    {
        Rng rng = new Rng(x, y);

        Color radianceSum = new Color(0f);

        Vec3 normalSum = Vec3.Zero;
        Vec3 normalFallback = Vec3.Zero;
        uint normalCount = 0;

        float depthSum = 0f;
        uint depthCount = 0;

        for (uint i = 0; i != _samples; ++i)
        {
            Vec2 pos = new Vec2((x + rng.NextFloat()) / Width, (y + rng.NextFloat()) / Height);
            Ray ray = _view.Ray(pos, _aspect);

            Fragment frag = _scene.Sample(ray, ref rng, _bounces);

            radianceSum += frag.Radiance;
            if (frag.Normal is Vec3 n)
            {
                Debug.Assert(n.IsUnit, "Invalid normal");
                normalSum += n;
                normalFallback = n;
                normalCount++;
            }
            if (frag.Depth is float d)
            {
                depthSum += d;
                depthCount++;
            }
        }

        Color radiance = radianceSum / _samples;
        Vec3? normal = normalCount > 0 ? normalSum.NormalizeOr(normalFallback) : null;
        float? depth = depthCount > 0 ? depthSum / depthCount : null;
        return new Fragment(radiance, normal, depth);
    }
}
