using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/**
 * Wavefront Obj.
 * Polygonal faces are supported (no curves or lines), materials are ignored at this time.
 * Format specification: http://www.martinreddy.net/gfx/3d/OBJ.spec
 * Faces are assumed to be convex and are triangulated using a simple triangle fan.
 *
 * NOTE: This doesn't do any handedness correction (as Obj doesn't specify the handedness), that
 * does mean that obj files exported from software with a right-handed coordinate system appear
 * flipped.
 */

struct ObjToken
{
    public enum Type { Word, Slash, Newline, End }

    public Type Kind;
    public string Value;

    public ObjToken(Type kind, string value = "")
    {
        Kind = kind;
        Value = value;
    }
}

class ObjLexer
{
    private StreamReader _reader;
    private ObjToken? _peeked;
    private StringBuilder _wordBuf = new StringBuilder();

    public ObjLexer(StreamReader reader) => _reader = reader;

    public ObjToken Peek()
    {
        if (_peeked is null)
            _peeked = Read();
        return _peeked.Value;
    }

    public ObjToken Next()
    {
        if (_peeked is ObjToken tok)
        {
            _peeked = null;
            return tok;
        }
        return Read();
    }

    public void SkipLine()
    {
        while (true)
        {
            switch (Next().Kind)
            {
                case ObjToken.Type.Newline:
                case ObjToken.Type.End:
                    return;
            }
        }
    }

    private ObjToken Read()
    {
        while (true)
        {
            int ch;
            switch (ch = _reader.Read())
            {
                case -1:
                    return new ObjToken(ObjToken.Type.End);
                case '\n':
                    return new ObjToken(ObjToken.Type.Newline);
                case '/':
                    return new ObjToken(ObjToken.Type.Slash);
                case ' ':
                case '\t':
                case '\r':
                    continue;
                case '#':
                    // Skip comment to end of line.
                    while (true)
                    {
                        int c = _reader.Read();
                        if (c == -1) return new ObjToken(ObjToken.Type.End);
                        if ((char)c == '\n') return new ObjToken(ObjToken.Type.Newline);
                    }
                default:
                    return ReadWord((char)ch);
            }
        }
    }

    private ObjToken ReadWord(char first)
    {
        _wordBuf.Clear();
        _wordBuf.Append(first);
        while (_reader.Peek() != -1 && !IsWordEnd((char)_reader.Peek()))
            _wordBuf.Append((char)_reader.Read());
        return new ObjToken(ObjToken.Type.Word, _wordBuf.ToString());
    }

    private static bool IsWordEnd(char ch) => ch switch
    {
        ' ' or '\t' or '\r' or '\n' or '/' => true,
        _ => false
    };
}

static class ObjLoader
{
    public static Mesh Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        return Parse(new ObjLexer(reader));
    }

    private static Mesh Parse(ObjLexer lexer)
    {
        var positions = new List<Vec3>();
        var faces = new List<int[]>();

        while (true)
        {
            ObjToken tok = lexer.Next();
            if (tok.Kind == ObjToken.Type.End)
                break;
            if (tok.Kind != ObjToken.Type.Word)
                continue;

            switch (tok.Value)
            {
                case "v":
                    positions.Add(ReadVec3(lexer));
                    lexer.SkipLine();
                    break;
                case "f":
                    faces.Add(ReadFace(lexer, positions.Count));
                    break;
                default:
                    lexer.SkipLine();
                    break;
            }
        }

        if (faces.Count == 0)
            throw new Exception("OBJ: no faces found");

        // Triangulate faces using a triangle fan from the first vertex.
        var triangles = new List<Triangle>();
        foreach (int[] face in faces)
        {
            Vec3 a = positions[face[0]];
            for (int i = 2; i < face.Length; ++i)
            {
                triangles.Add(new Triangle(a, positions[face[i - 1]], positions[face[i]]));
            }
        }

        return new Mesh(triangles);
    }

    private static int[] ReadFace(ObjLexer lexer, int posCount)
    {
        var indices = new List<int>();
        while (lexer.Peek().Kind == ObjToken.Type.Word)
        {
            indices.Add(ReadIndex(lexer, posCount));

            // Skip optional slash-separated texcoord and normal indices.
            if (lexer.Peek().Kind == ObjToken.Type.Slash)
            {
                lexer.Next(); // First slash
                if (lexer.Peek().Kind == ObjToken.Type.Word)
                    lexer.Next(); // Texcoord index
                if (lexer.Peek().Kind == ObjToken.Type.Slash)
                {
                    lexer.Next(); // Second slash
                    if (lexer.Peek().Kind == ObjToken.Type.Word)
                        lexer.Next(); // Normal index
                }
            }
        }

        if (indices.Count < 3)
            throw new Exception("OBJ: face has fewer than 3 vertices");

        return indices.ToArray();
    }

    private static float ReadFloat(ObjLexer lexer)
    {
        ObjToken tok = lexer.Next();
        if (tok.Kind != ObjToken.Type.Word)
            throw new Exception($"OBJ: expected number, got {tok.Kind}");
        if (!float.TryParse(tok.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            throw new Exception($"OBJ: invalid number '{tok.Value}'");
        return val;
    }

    private static Vec3 ReadVec3(ObjLexer lexer) =>
        new Vec3(ReadFloat(lexer), ReadFloat(lexer), ReadFloat(lexer));

    private static int ReadIndex(ObjLexer lexer, int count)
    {
        ObjToken tok = lexer.Next();
        if (tok.Kind != ObjToken.Type.Word)
            throw new Exception($"OBJ: expected index, got {tok.Kind}");
        if (!int.TryParse(tok.Value, out int idx))
            throw new Exception($"OBJ: invalid index '{tok.Value}'");

        // OBJ indices are 1-based; negative indices are relative to the end.
        idx = idx < 0 ? count + idx : idx - 1;
        if (idx < 0 || idx >= count)
            throw new Exception($"OBJ: index out of bounds ({idx}, count: {count})");
        return idx;
    }
}
