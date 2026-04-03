using System;
using System.Diagnostics;
using System.Threading;

class Renderer
{
    public uint Width { get; }
    public uint Height { get; }
    public View View { get; }
    public Color[] Radiance { get; }
    public Vec3[] Normals { get; }
    public Vec2[] Surface { get; }
    public float[] Depth { get; }

    private Scene _scene;
    private float _aspect;

    private uint _blockSize;
    private uint _blockCountX, _blockCountY, _blockCountTotal;

    private int _blockNext;
    private volatile int _blockCompleted;
    private SemaphoreSlim _blockSignal;

    private uint _samples;
    private uint _bounces;
    private Counters _counters;

    public Renderer(
        Scene scene,
        View view,
        uint width,
        uint height,
        uint blockSize,
        uint samples,
        uint bounces,
        Counters counters)
    {
        Debug.Assert(width > 0 && height > 0);
        Debug.Assert(blockSize > 0);
        Debug.Assert(samples > 0);

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

        _samples = samples;
        _bounces = bounces;
        _counters = counters;

        scene.Build(counters);

        Radiance = new Color[Width * Height];
        Normals = new Vec3[Width * Height];
        Surface = new Vec2[Width * Height];
        Depth = new float[Width * Height];

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

        uint pixelCount = (xMax - xMin) * (yMax - yMin);

        // Per-pixel accumulation buffers (across samples).
        Color[] radianceSum = new Color[pixelCount];
        Vec3[] normalSum = new Vec3[pixelCount];
        Vec3[] normalFallback = new Vec3[pixelCount];
        uint[] normalCount = new uint[pixelCount];
        float[] depthSum = new float[pixelCount];
        uint[] depthCount = new uint[pixelCount];
        Vec2?[] surface = new Vec2?[pixelCount];

        // Per-ray wavefront state.
        Ray[] rays = new Ray[pixelCount];
        Color[] energy = new Color[pixelCount];
        bool[] active = new bool[pixelCount];
        Rng[] rngs = new Rng[pixelCount];
        Surface[] surfaces = new Surface[pixelCount];
        Ray[] shadowRays = new Ray[pixelCount];
        bool[] shadowNeeded = new bool[pixelCount];
        float[] shadowCosTheta = new float[pixelCount];
        bool[] occluded = new bool[pixelCount];

        // Initialize one Rng per pixel.
        for (uint py = yMin; py != yMax; ++py)
            for (uint px = xMin; px != xMax; ++px)
                rngs[(py - yMin) * (xMax - xMin) + (px - xMin)] = new Rng(px, py);

        _counters.Bump(Counters.Type.Pixel, (int)pixelCount);

        for (uint s = 0; s != _samples; ++s)
        {
            _counters.Bump(Counters.Type.Sample, (int)pixelCount);

            // Initialize rays from camera for this sample.
            for (uint py = yMin; py != yMax; ++py)
            {
                for (uint px = xMin; px != xMax; ++px)
                {
                    uint r = (py - yMin) * (xMax - xMin) + (px - xMin);
                    ref Rng rng = ref rngs[r];
                    Vec2 pos = new Vec2((px + rng.NextFloat()) / Width, (py + rng.NextFloat()) / Height);
                    rays[r] = View.Ray(pos, _aspect);
                    energy[r] = Color.White;
                    active[r] = true;
                }
            }

            for (uint bounce = 0; bounce != (_bounces + 1); ++bounce)
            {
                _counters.Bump(Counters.Type.SampleBounce, (int)pixelCount);

                // Trace all rays at once.
                _scene.Trace(rays, surfaces);

                // Prepare shadow rays and scatter.
                for (int r = 0; r != pixelCount; ++r)
                {
                    if (!active[r]) continue;

                    bool isPrimary = bounce == 0 && s == 0;
                    Surface surf = surfaces[r];

                    radianceSum[r] += surf.Radiance * energy[r];

                    float roughness = 1.0f;
                    if (surf.Material is Material material)
                    {
                        Color specularColor = Color.Lerp(Color.White, material.Color, material.Metallic);
                        energy[r] *= Color.Lerp(material.Color, specularColor, 1f - roughness);
                        roughness = material.Roughness;
                    }

                    shadowNeeded[r] = false;

                    if (surf.Hit is ShapeHit hit)
                    {
                        _counters.Bump(Counters.Type.SampleHit);

                        if (isPrimary)
                        {
                            normalSum[r] += hit.Norm;
                            normalFallback[r] = hit.Norm;
                            normalCount[r]++;
                            depthSum[r] += hit.Dist;
                            depthCount[r]++;
                            surface[r] ??= hit.Surface;
                        }

                        Vec3 shadingNorm = Vec3.Dot(hit.Norm, rays[r].Dir) > 0f ? -hit.Norm : hit.Norm;
                        Vec3 hitPos = rays[r][hit.Dist] + shadingNorm * 1e-4f;

                        // Prepare shadow ray.
                        ref Rng rng = ref rngs[r];
                        Vec3 sunDir = _scene.Sky.SunSampleDir(ref rng);
                        float cosTheta = Vec3.Dot(shadingNorm, sunDir);
                        if (cosTheta > 0f && roughness > 0.05f)
                        {
                            shadowRays[r] = new Ray(hitPos, sunDir);
                            shadowNeeded[r] = true;
                            shadowCosTheta[r] = cosTheta;
                        }
                        else
                            _counters.Bump(Counters.Type.ShadowRaySkipped);

                        // Russian roulette.
                        if (bounce >= 3)
                        {
                            float survive = MathF.Max(energy[r].R, MathF.Max(energy[r].G, energy[r].B));
                            if (rng.NextFloat() >= survive)
                            {
                                _counters.Bump(Counters.Type.SampleTerminate);
                                active[r] = false;
                                continue;
                            }
                            energy[r] /= survive;
                        }

                        // Scatter.
                        Vec3 scatterDirDiffuse = (shadingNorm + Vec3.RandOnSphere(ref rng)).NormalizeOr(shadingNorm);
                        Vec3 scatterDirSpecular = Vec3.Reflect(rays[r].Dir, shadingNorm);
                        Vec3 scatterDir = Vec3.Lerp(scatterDirSpecular, scatterDirDiffuse, roughness).NormalizeOr(shadingNorm);
                        rays[r] = new Ray(hitPos, scatterDir);
                    }
                    else
                    {
                        _counters.Bump(Counters.Type.SampleMiss);

                        // Sky miss: primary ray adds sun contribution.
                        if (bounce == 0)
                            radianceSum[r] += _scene.Sky.SunRadianceRay(rays[r]) * energy[r];
                        active[r] = false;
                    }
                }

                // Batch shadow ray occlusion. Fill unused slots with a far-away ray so BVH bounds tests fail fast.
                Ray dummyRay = new Ray(new Vec3(1e10f, 1e10f, 1e10f), Vec3.Up);
                for (int r = 0; r != pixelCount; ++r)
                    if (!shadowNeeded[r]) shadowRays[r] = dummyRay;
                _scene.Occluded(shadowRays, occluded);

                // Apply shadow results.
                for (int r = 0; r != pixelCount; ++r)
                {
                    if (!shadowNeeded[r]) continue;
                    if (occluded[r])
                        _counters.Bump(Counters.Type.ShadowRayOccluded);
                    else
                        radianceSum[r] += _scene.Sky.SunRadiance * energy[r] * shadowCosTheta[r];
                }

                // Early out if all rays are inactive.
                bool anyActive = false;
                for (int r = 0; r != pixelCount; ++r)
                    anyActive |= active[r];
                if (!anyActive) break;
            }
        }

        // Write results.
        for (uint py = yMin; py != yMax; ++py)
        {
            for (uint px = xMin; px != xMax; ++px)
            {
                uint r = (py - yMin) * (xMax - xMin) + (px - xMin);
                uint pixIdx = py * Width + px;

                Radiance[pixIdx] = radianceSum[r] / _samples;
                Normals[pixIdx] = normalCount[r] > 0 ? normalSum[r].NormalizeOr(normalFallback[r]) : Vec3.Zero;
                Surface[pixIdx] = surface[r] ?? Vec2.Zero;
                Depth[pixIdx] = depthCount[r] > 0 ? depthSum[r] / depthCount[r] : float.PositiveInfinity;
            }
        }
    }
}
