using System;
using System.Diagnostics;
using System.Threading;

class Renderer
{
    private readonly record struct RenderFragment(
        Fragment Fragment, uint SampleCount);

    public uint Width { get; }
    public uint Height { get; }
    public View View { get; }
    public Color[] Radiance { get; }
    public Vec3[] Normals { get; }
    public Vec2[] Uv { get; }
    public float[] Depth { get; }
    public uint[] Samples { get; }

    private Scene _scene;
    private float _aspect;

    private uint _blockSize;
    private uint _blockCountX, _blockCountY, _blockCountTotal;

    private int _blockNext;
    private volatile int _blockCompleted;
    private SemaphoreSlim _blockSignal;

    private uint _minSamples;
    private uint _maxSamples;
    private float _varianceThreshold;
    private uint _bounces;
    private float _indirectClamp;

    private Counters _counters;

    public Renderer(
        Scene scene,
        View view,
        uint width,
        uint height,
        uint blockSize,
        uint minSamples,
        uint maxSamples,
        float varianceThreshold,
        uint bounces,
        float indirectClamp,
        Counters counters)
    {
        Debug.Assert(width > 0 && height > 0);
        Debug.Assert(blockSize > 0);
        Debug.Assert(minSamples > 0 && minSamples <= maxSamples);

        Width = width;
        Height = height;
        View = view;

        _scene = scene;
        _aspect = (float)width / (float)height;

        _blockSize = blockSize;
        _blockCountX = (width + blockSize - 1) / blockSize;
        _blockCountY = (height + blockSize - 1) / blockSize;
        _blockCountTotal = _blockCountX * _blockCountY;
        _blockSignal = new SemaphoreSlim(0);

        _minSamples = minSamples;
        _maxSamples = maxSamples;
        _varianceThreshold = varianceThreshold;
        _bounces = bounces;
        _indirectClamp = indirectClamp;
        _counters = counters;

        scene.Build(counters);

        Radiance = new Color[Width * Height];
        Normals = new Vec3[Width * Height];
        Uv = new Vec2[Width * Height];
        Depth = new float[Width * Height];
        Samples = new uint[Width * Height];

        // Start the worker threads.
        for (int i = 0; i < Environment.ProcessorCount; ++i)
        {
            Thread thread = new Thread(RenderWorker)
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

    private void RenderWorker()
    {
        _counters.Bump(Counters.Type.Worker);
        while (true)
        {
            int block = Interlocked.Increment(ref _blockNext) - 1;
            if ((uint)block >= _blockCountTotal)
                break;

            Execute((uint)block);

            Interlocked.Increment(ref _blockCompleted);
            _blockSignal.Release();
        }
        _counters.Flush();
    }

    private void Execute(uint block)
    {
        Debug.Assert(block < _blockCountTotal);
        _counters.Bump(Counters.Type.Block);

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
                RenderFragment result = Render(x, y);

                Radiance[y * Width + x] = result.Fragment.Radiance;
                Normals[y * Width + x] = result.Fragment.Normal ?? Vec3.Zero;
                Uv[y * Width + x] = result.Fragment.Uv ?? Vec2.Zero;
                Depth[y * Width + x] = result.Fragment.Depth ?? float.PositiveInfinity;
                Samples[y * Width + x] = result.SampleCount;
            }
        }
    }

    private RenderFragment Render(uint x, uint y)
    {
        _counters.Bump(Counters.Type.Pixel);
        Rng rng = new Rng(x, y);

        Color radianceSum = new Color(0f);
        float lumSum = 0f, lumSumSqr = 0f;

        Vec3 normalSum = Vec3.Zero;
        Vec3 normalFallback = Vec3.Zero;
        uint normalCount = 0;

        Vec2? uv = null;

        float depthSum = 0f;
        uint depthCount = 0;

        uint sampleIndex = 0;
        for (; sampleIndex != _maxSamples; ++sampleIndex)
        {
            Vec2 pos = new Vec2((x + rng.NextFloat()) / Width, (y + rng.NextFloat()) / Height);
            Ray ray = View.Ray(pos, _aspect);

            Fragment frag = _scene.Sample(ray, ref rng, _bounces, _indirectClamp, _counters);

            radianceSum += frag.Radiance;

            float lum = frag.Radiance.Luminance;
            lumSum += lum;
            lumSumSqr += lum * lum;

            if (frag.Normal is Vec3 norm)
            {
                Debug.Assert(norm.IsUnit, "Invalid normal");
                normalSum += norm;
                normalFallback = norm;
                normalCount++;
            }
            if (frag.Depth is float d)
            {
                depthSum += d;
                depthCount++;
            }
            uv ??= frag.Uv;

            if (sampleIndex >= _minSamples - 1)
            {
                if (RelativeStdErr(lumSum, lumSumSqr, sampleIndex + 1) < _varianceThreshold)
                {
                    break; // Converged enough.
                }
            }
        }

        // Combine the fragments of the individual samples into a final combined fragment.
        uint sampleCount = sampleIndex + 1;
        Color radiance = radianceSum / sampleCount;
        Vec3? normal = normalCount > 0 ? normalSum.NormalizeOr(normalFallback) : null;
        float? depth = depthCount > 0 ? depthSum / depthCount : null;
        Fragment combinedFrag = new Fragment(radiance, normal, uv, depth);

        return new RenderFragment(combinedFrag, sampleCount);
    }

    // Relative standard error of the mean: stddev(mean) / mean. Lower = more converged.
    private static float RelativeStdErr(float sum, float sumSqr, uint n)
    {
        float mean = sum / n;
        float variance = MathF.Max(0f, sumSqr / n - mean * mean);
        return MathF.Sqrt(variance / n) / MathF.Max(mean, 1e-4f);
    }
}
