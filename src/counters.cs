using System;
using System.Text;
using System.Threading;

enum Counter
{
    TraceRay,
    OccludeRay,
    SampleBounce,
    SampleHit,

    _Count
}

class Counters
{
    private readonly long[] _data = new long[(int)Counter._Count];

    public void Bump(Counter c) => Interlocked.Increment(ref _data[(int)c]);
    public long Get(Counter c) => Interlocked.Read(ref _data[(int)c]);

    public string Dump()
    {
        int maxNameLen = 0;
        for (int i = 0; i != (int)Counter._Count; ++i)
        {
            maxNameLen = Math.Max(maxNameLen, ((Counter)i).ToString().Length);
        }

        var sb = new StringBuilder();
        for (int i = 0; i != (int)Counter._Count; ++i)
        {
            string name = ((Counter)i).ToString();
            sb.Append(name);
            sb.Append(' ', maxNameLen - name.Length);
            sb.Append(": ");
            sb.Append(FormatNum(Get((Counter)i)));
            if (i != (int)Counter._Count - 1)
            {
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string FormatNum(long n)
    {
        if (n < 1_000)
            return n.ToString();
        if (n < 1_000_000)
            return $"{n / 1_000.0:F1}K";
        return $"{n / 1_000_000.0:F1}M";
    }
}
