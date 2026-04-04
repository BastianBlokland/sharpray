using System;
using System.Diagnostics;

// Cumulative Distribution Function
// Lookup weighted indices based on a normalized value.
// https://en.wikipedia.org/wiki/Cumulative_distribution_function
class Cdf1
{
    public float TotalWeight { get; }
    public uint Count => (uint)_cdf.Length - 1;

    private readonly float[] _cdf; // n+1 entries.

    public Cdf1(ReadOnlySpan<float> weights)
    {
        _cdf = new float[weights.Length + 1];

        // Compute prefix sum.
        _cdf[0] = 0f;
        for (int i = 0; i != weights.Length; ++i)
        {
            _cdf[i + 1] = _cdf[i] + weights[i];
        }
        TotalWeight = _cdf[weights.Length];

        if (TotalWeight > 0f)
        {
            // Normalize.
            for (int i = 1; i != _cdf.Length; ++i)
            {
                _cdf[i] /= TotalWeight;
            }
        }
    }

    public uint Sample(float norm)
    {
        Debug.Assert(norm >= 0f && norm < 1f);
        uint lo = 0;
        uint hi = Count;
        while (lo < hi)
        {
            uint mid = (lo + hi) >> 1;
            if (_cdf[mid + 1] <= norm)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return Math.Min(lo, Count - 1);
    }

    public uint SampleRand(ref Rng rng) => Sample(rng.NextFloat());
}

// Cumulative Distribution Function
// Lookup indices into a 2d grid of weighed values based on normalized x,y values.
// https://en.wikipedia.org/wiki/Cumulative_distribution_function
class Cdf2
{
    public float TotalWeight => _marginal.TotalWeight;

    private readonly Cdf1 _marginal; // Probability of picking a row.
    private readonly Cdf1[] _conditionals; // Probability of picking a column.

    public Cdf2(Vec2i size, Func<Vec2i, float> weightAt)
    {
        _conditionals = new Cdf1[size.Y];

        float[] rowWeights = new float[size.Y];
        float[] colWeights = new float[size.X];
        for (int y = 0; y != size.Y; ++y)
        {
            for (int x = 0; x != size.X; ++x)
            {
                colWeights[x] = weightAt(new Vec2i(x, y));
            }
            _conditionals[y] = new Cdf1(colWeights);
            rowWeights[y] = _conditionals[y].TotalWeight;
        }
        _marginal = new Cdf1(rowWeights);
    }

    // Returns texel coordinate sampled proportionally to the weight function.
    public Vec2i Sample(Vec2 coord)
    {
        Debug.Assert(coord.X >= 0f && coord.X < 1f);
        Debug.Assert(coord.Y >= 0f && coord.Y < 1f);
        int y = (int)_marginal.Sample(coord.Y);
        int x = (int)_conditionals[y].Sample(coord.X);
        return new Vec2i(x, y);
    }

    public Vec2i SampleRand(ref Rng rng) => Sample(Vec2.Rand(ref rng));
}
