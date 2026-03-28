using System.Diagnostics;

class Renderer
{
    public Image Result { get; }

    private Scene _scene;
    private View _view;
    private uint _width, _height;
    private float _aspect;

    public Renderer(Scene scene, View view, uint width, uint height)
    {
        Debug.Assert(width > 0 && height > 0);
        _scene = scene;
        _view = view;
        _width = width;
        _height = height;
        _aspect = (float)_width / (float)_height;

        Result = new Image(width, height);
    }

    public (uint Step, uint Total) Tick()
    {
        for (uint y = 0; y != _height; ++y)
        {
            for (uint x = 0; x != _width; ++x)
            {
                Result.Pixels[y * _width + x] = Render(x, y);
            }
        }
        return (0, 0);
    }

    private Pixel Render(uint x, uint y)
    {
        Rng rng = new Rng(x, y);
        Vec2 pos = new Vec2((x + 0.5f) / _width, (y + 0.5f) / _height);
        Ray ray = _view.Ray(pos, _aspect);
        TraceResult result = _scene.Trace(ray, ref rng);
        return result.Radiance.ToPixel();
    }
}
