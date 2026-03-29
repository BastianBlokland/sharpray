using System;
using System.Diagnostics;
using System.Threading;

class Renderer
{
    public Color[] Radiance { get; }
    public Vec3[] Normals { get; }

    private Scene _scene;
    private View _view;
    private uint _width, _height;
    private float _aspect;

    private uint _blockSize;
    private uint _blockCountX, _blockCountY, _blockCountTotal;

    private int _blockNext;
    private volatile int _blockCompleted;
    private SemaphoreSlim _blockSignal;

    private uint _samples;
    private uint _bounches;

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

        _scene = scene;
        _view = view;
        _width = width;
        _height = height;
        _aspect = (float)_width / (float)_height;

        _blockSize = blockSize;
        _blockCountX = (_width + _blockSize - 1) / _blockSize;
        _blockCountY = (_height + _blockSize - 1) / _blockSize;
        _blockCountTotal = _blockCountX * _blockCountY;
        _blockSignal = new SemaphoreSlim(0);

        _samples = samples;
        _bounches = bounces;

        scene.Lock();

        Radiance = new Color[width * height];
        Normals = new Vec3[width * height];

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
        uint xMax = Math.Min(xMin + _blockSize, _width);
        uint yMax = Math.Min(yMin + _blockSize, _height);

        for (uint y = yMin; y != yMax; ++y)
        {
            for (uint x = xMin; x != xMax; ++x)
            {
                (Color radiance, Vec3 normal) = Render(x, y);

                Radiance[y * _width + x] = radiance;
                Normals[y * _width + x] = normal;
            }
        }
    }

    private (Color Radiance, Vec3 Normal) Render(uint x, uint y)
    {
        Rng rng = new Rng(x, y);
        Color radianceSum = new Color(0f);
        Vec3 normalSum = Vec3.Zero;

        for (uint i = 0; i != _samples; ++i)
        {
            Vec2 pos = new Vec2((x + rng.NextFloat()) / _width, (y + rng.NextFloat()) / _height);
            Ray ray = _view.Ray(pos, _aspect);

            var (radiance, normal) = _scene.Sample(ray, ref rng, _bounches);

            radianceSum += radiance;
            if (normal is Vec3 n)
                normalSum += n;
        }

        return (radianceSum / _samples, normalSum.NormalizeOr(Vec3.Zero));
    }
}
