using System.Diagnostics;
using System.Text;

struct InfoWriter
{
    private StringBuilder _sb;
    private int _indent;

    public InfoWriter(int indent = 0)
    {
        _sb = new StringBuilder();
        _indent = indent;
    }

    public void Clear()
    {
        _sb.Clear();
        _indent = 0;
    }

    public void Indent() => _indent++;
    public void Outdent()
    {
        Debug.Assert(_indent > 0, "Indent underflow");
        _indent--;
    }

    public void WriteLine(string text) => _sb.AppendLine(new string(' ', _indent * 2) + text);

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
