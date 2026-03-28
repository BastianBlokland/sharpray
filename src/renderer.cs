using System;
using System.Diagnostics;
using System.Threading;

class Renderer
{
    public Image Result { get; }

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

        Result = new Image(width, height);

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
                Result.Pixels[y * _width + x] = Render(x, y);
            }
        }
    }

    private Pixel Render(uint x, uint y)
    {
        Rng rng = new Rng(x, y);
        Color radiance = new Color(0f);
        for (uint i = 0; i != _samples; ++i)
        {
            Vec2 pos = new Vec2((x + rng.NextFloat()) / _width, (y + rng.NextFloat()) / _height);
            Ray ray = _view.Ray(pos, _aspect);
            radiance += _scene.Sample(ray, ref rng, _bounches).Radiance;
        }
        return Tonemap(radiance / _samples).ToPixel();
    }

    private static Color Tonemap(Color radiance)
    {
        // Linear with shoulder region.
        // By user SteveM in comment section on https://mynameismjp.wordpress.com.
        // https://mynameismjp.wordpress.com/2010/04/30/a-closer-look-at-tone-mapping/#comment-118287

        const float a = 1.8f; // mid.
        const float b = 1.4f; // toe.
        const float c = 0.5f; // shoulder.
        const float d = 1.5f; // mid.
        return (radiance * (a * radiance + new Color(b))) / (radiance * (a * radiance + new Color(c)) + new Color(d));
    }
}
