using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

ref struct InfoWriter
{
    [InterpolatedStringHandler]
    public struct Formatter
    {
        private StringBuilder _sb;

        public Formatter(int literalLength, int formattedCount, InfoWriter writer)
        {
            _sb = writer._sb;
            _sb.Append(' ', writer.Indent * 2);
        }

        public void AppendLiteral(string s) => _sb.Append(s);

        public void AppendFormatted<T>(T value)
        {
            if (value is ISpanFormattable spanFormattable)
            {
                Span<char> buffer = stackalloc char[64];
                spanFormattable.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
                _sb.Append(buffer[..written]);
            }
            else
            {
                _sb.Append(value);
            }
        }

        public void AppendFormatted<T>(T value, string format) where T : IFormattable
        {
            if (value is ISpanFormattable spanFormattable)
            {
                Span<char> buffer = stackalloc char[64];
                spanFormattable.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture);
                _sb.Append(buffer[..written]);
            }
            else
            {
                _sb.Append(value.ToString(format, CultureInfo.InvariantCulture));
            }
        }
    }

    public int Indent { get; private set; }

    private StringBuilder _sb;

    public InfoWriter(int indent = 0)
    {
        Indent = indent;
        _sb = new StringBuilder();
    }

    public void Clear()
    {
        Indent = 0;
        _sb.Clear();
    }

    public void IndentPush() => ++Indent;
    public void IndentPop()
    {
        Debug.Assert(Indent > 0, "Indent underflow");
        --Indent;
    }

    public void WriteLine(string text) => _sb.AppendLine(new string(' ', Indent * 2) + text);
    public void WriteLine([InterpolatedStringHandlerArgument("")] ref Formatter f) => _sb.AppendLine();

    public void Separate(int lines = 1)
    {
        int remainingNewlines = lines + ((_sb.Length == 0) ? 0 : 1);
        for (int i = _sb.Length; i-- != 0;)
        {
            switch (_sb[i])
            {
                case '\r':
                    continue;
                case '\n':
                    if (--remainingNewlines == 0)
                        return;
                    continue;
            }
            break;
        }
        while (remainingNewlines-- != 0)
            _sb.AppendLine();
    }

    public override string ToString() => _sb.ToString();
}
