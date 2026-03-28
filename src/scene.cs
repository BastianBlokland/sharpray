using System;

struct TraceResult
{
    public RayHit? Hit;
    public Color Radiance;

    public TraceResult(RayHit? hit, Color radiance)
    {
        Hit = hit;
        Radiance = radiance;
    }
}

class Scene
{
    public Scene()
    {
    }

    public TraceResult Trace(Ray ray)
    {
        Color skyRadiance = SkyRadiance();
        return new TraceResult(null, skyRadiance);
    }

    private Color SkyRadiance()
    {
        return new Color(0f, 1f, 0f);
    }
}
