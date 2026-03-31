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

    public override string ToString() => _sb.ToString();
}
