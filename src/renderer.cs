using System;
using System.Diagnostics;

class Renderer
{
    public Image Result { get; }

    private Scene _scene;
    private View _view;
    private uint _width, _height;
    private float _aspect;

    private uint _blockSize;
    private uint _blockCountX, _blockCountY, _blockCountTotal;
    private uint _blockCurrent;

    public Renderer(Scene scene, View view, uint width, uint height, uint blockSize)
    {
        Debug.Assert(width > 0 && height > 0);
        Debug.Assert(blockSize > 0);
        _scene = scene;
        _view = view;
        _width = width;
        _height = height;
        _aspect = (float)_width / (float)_height;

        _blockSize = blockSize;
        _blockCountX = (_width + _blockSize - 1) / _blockSize;
        _blockCountY = (_height + _blockSize - 1) / _blockSize;
        _blockCountTotal = _blockCountX * _blockCountY;

        Result = new Image(width, height);
    }

    public (uint Step, uint Total) Tick()
    {
        if (_blockCurrent >= _blockCountTotal)
            return (_blockCountTotal, _blockCountTotal);

        Execute(_blockCurrent++);

        return (_blockCurrent, _blockCountTotal);
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
        Vec2 pos = new Vec2((x + 0.5f) / _width, (y + 0.5f) / _height);
        Ray ray = _view.Ray(pos, _aspect);
        TraceResult result = _scene.Trace(ray, ref rng);
        return Tonemap(result.Radiance).ToPixel();
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
