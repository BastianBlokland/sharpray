using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/**
 * Wavefront Obj.
 * Polygonal faces are supported (no curves or lines).
 * Format specification: http://www.martinreddy.net/gfx/3d/OBJ.spec
 * Faces are assumed to be convex and are triangulated using a simple triangle fan.
 * Materials: mtllib/usemtl are supported. PBR properties (Kd, Ke, Pr, Pm, Ns) are parsed from .mtl files.
 *
 * NOTE: This doesn't do any handedness correction (as Obj doesn't specify the handedness), that
 * does mean that obj files exported from software with a right-handed coordinate system appear
 * flipped.
 */

enum ObjToken { Word, Slash, Newline, End }

class ObjLexer
{
    private StreamReader _reader;
    private bool _hasPeek;
    private ObjToken _peekKind;
    private char[] _peekWordBuf = new char[512];
    private int _peekWordLen;

    public ObjLexer(StreamReader reader) => _reader = reader;

    public ObjToken Peek()
    {
        if (!_hasPeek)
        {
            _peekKind = Read(_peekWordBuf, out _peekWordLen);
            _hasPeek = true;
        }
        return _peekKind;
    }

    public ObjToken Next(Span<char> wordBuf, out int wordLen)
    {
        if (_hasPeek)
        {
            _hasPeek = false;
            _peekWordBuf.AsSpan(0, _peekWordLen).CopyTo(wordBuf);
            wordLen = _peekWordLen;
            return _peekKind;
        }
        return Read(wordBuf, out wordLen);
    }

    public ObjToken Next()
    {
        if (_hasPeek)
        {
            _hasPeek = false;
            return _peekKind;
        }
        Span<char> scratch = stackalloc char[64];
        return Read(scratch, out _);
    }

    public void SkipLine()
    {
        ObjToken k;
        do
        {
            k = Next();
        } while (k != ObjToken.Newline && k != ObjToken.End);
    }

    private ObjToken Read(Span<char> wordBuf, out int wordLen)
    {
        wordLen = 0;
        while (true)
        {
            int ch;
            switch (ch = _reader.Read())
            {
                case -1: return ObjToken.End;
                case '\n': return ObjToken.Newline;
                case '/': return ObjToken.Slash;
                case ' ':
                case '\t':
                case '\r':
                    continue;
                case '#':
                    // Skip comment to end of line.
                    while (true)
                    {
                        int c = _reader.Read();
                        if (c == -1)
                        {
                            return ObjToken.End;
                        }
                        if ((char)c == '\n')
                        {
                            return ObjToken.Newline;
                        }
                    }
                default:
                    return ReadWord((char)ch, wordBuf, out wordLen);
            }
        }
    }

    private ObjToken ReadWord(char first, Span<char> buf, out int len)
    {
        int i = 0;
        buf[i++] = first;
        int c;
        while ((c = _reader.Peek()) != -1 && !IsWordEnd((char)c))
        {
            buf[i++] = (char)_reader.Read();
        }
        len = i;
        return ObjToken.Word;
    }

    private static bool IsWordEnd(char ch) => ch switch
    {
        ' ' or '\t' or '\r' or '\n' or '/' => true,
        _ => false
    };
}

static class ObjLoader
{
    public static void Load(
        string path,
        Scene scene,
        Transform? transform = null,
        Material? material = null,
        Counters counters)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, bufferSize: 65536);
        using (counters.TimeScope(Counters.Type.TimeObjLoad))
        {
            Parse(new ObjLexer(reader), path, scene, transform, material, counters);
        }
    }

    private static void Parse(
        ObjLexer lexer,
        string path,
        Scene scene,
        Transform? transform,
        Material? material,
        Counters counters)
    {
        var positions = new List<Vec3>();
        var normals = new List<Vec3>();
        var triangles = new List<Triangle>();
        var faceEntries = new List<(int Pos, int Norm)>();
        var materials = new Dictionary<string, Material>();
        string fileName = Path.GetFileNameWithoutExtension(path);
        string? currentMaterialName = null;
        string? currentObjectName = null;
        int objectCount = 0;

        void BuildObject()
        {
            if (triangles.Count == 0)
                return;

            string name = currentObjectName is string n ? $"{fileName}_{n}" : $"{fileName}_{objectCount}";
            Transform objTrans = transform ?? Transform.Identity();
            Material objMat;
            if (currentMaterialName == null || !materials.TryGetValue(currentMaterialName, out objMat))
            {
                objMat = material ?? new Material(Color.White, 1.0f);
            }
            Mesh mesh = new Mesh(triangles, counters);

            scene.AddObject(new Object(name, objTrans, objMat, mesh));

            triangles.Clear();
            objectCount++;
        }

        Span<char> wordBuf = stackalloc char[512];
        while (true)
        {
            ObjToken kind = lexer.Next(wordBuf, out int wordLen);
            if (kind == ObjToken.End)
                break;
            if (kind != ObjToken.Word)
                continue;

            var word = wordBuf[..wordLen];
            if (word.SequenceEqual("v"))
            {
                positions.Add(ReadVec3(lexer));
                lexer.SkipLine();
            }
            else if (word.SequenceEqual("vn"))
            {
                normals.Add(ReadVec3(lexer));
                lexer.SkipLine();
            }
            else if (word.SequenceEqual("f"))
            {
                faceEntries.Clear();
                ReadFace(lexer, positions.Count, normals.Count, faceEntries);
                TriangulateFace(faceEntries, positions, normals, triangles);
            }
            else if (word.SequenceEqual("o") || word.SequenceEqual("g"))
            {
                BuildObject();
                if (lexer.Peek() == ObjToken.Word)
                {
                    lexer.Next(wordBuf, out int nameLen);
                    currentObjectName = wordBuf[..nameLen].ToString();
                }
                lexer.SkipLine();
            }
            else if (word.SequenceEqual("mtllib"))
            {
                lexer.Next(wordBuf, out int nameLen);
                string mtlPath = Path.Combine(Path.GetDirectoryName(path)!, wordBuf[..nameLen].ToString());
                if (File.Exists(mtlPath))
                    ParseMtl(mtlPath, materials);
                lexer.SkipLine();
            }
            else if (word.SequenceEqual("usemtl"))
            {
                BuildObject();
                lexer.Next(wordBuf, out int nameLen);
                currentMaterialName = wordBuf[..nameLen].ToString();
                lexer.SkipLine();
            }
            else
            {
                lexer.SkipLine();
            }
        }

        BuildObject();

        if (objectCount == 0)
            throw new Exception("OBJ: no objects found");
    }

    private static void ParseMtl(string path, Dictionary<string, Material> output)
    {
        string? name = null;
        Color color = Color.White;
        Color radiance = default;
        float roughness = 1f;
        float metallic = 0f;
        bool hasPr = false; // Physical roughness.
        float ns = 0f; // Shininess.

        void BuildMaterial()
        {
            if (name == null)
                return;
            float matRoughness = Math.Clamp(hasPr ? roughness : MathF.Sqrt(2f / (ns + 2f)), 0, 1);
            output[name] = new Material(color, matRoughness, metallic, radiance);
        }

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        var lexer = new ObjLexer(reader);

        Span<char> wordBuf = stackalloc char[512];
        while (true)
        {
            ObjToken kind = lexer.Next(wordBuf, out int wordLen);
            if (kind == ObjToken.End)
                break;
            if (kind != ObjToken.Word)
                continue;

            var word = wordBuf[..wordLen];
            if (word.SequenceEqual("newmtl"))
            {
                BuildMaterial();

                lexer.Next(wordBuf, out int nameLen);
                name = wordBuf[..nameLen].ToString();
                color = Color.White;
                radiance = default;
                roughness = 1f;
                metallic = 0f;
                hasPr = false;
                ns = 0f;

                lexer.SkipLine();
            }
            else if (word.SequenceEqual("Kd"))
            {
                color = ReadColor(lexer);
                lexer.SkipLine();
            }
            else if (word.SequenceEqual("Ke"))
            {
                radiance = ReadColor(lexer);
                lexer.SkipLine();
            }
            else if (word.SequenceEqual("Pr"))
            {
                roughness = ReadFloat(lexer);
                hasPr = true;
                lexer.SkipLine();
            }
            else if (word.SequenceEqual("Pm"))
            {
                metallic = ReadFloat(lexer);
                lexer.SkipLine();
            }
            else if (word.SequenceEqual("Ns"))
            {
                ns = ReadFloat(lexer);
                lexer.SkipLine();
            }
            else
            {
                lexer.SkipLine();
            }
        }
        BuildMaterial();
    }

    private static void TriangulateFace(
        IReadOnlyList<(int Pos, int Norm)> entries,
        IReadOnlyList<Vec3> positions,
        IReadOnlyList<Vec3> normals,
        List<Triangle> output)
    {
        if (entries.Count < 3)
            throw new Exception("OBJ: face has fewer than 3 vertices");

        // Triangle fan from the first vertex.

        (int facePosA, int faceNormA) = entries[0];
        Vec3 triPosA = positions[facePosA];

        for (int i = 2; i < entries.Count; ++i)
        {
            (int facePosB, int faceNormB) = entries[i - 1];
            (int facePosC, int faceNormC) = entries[i];

            Vec3 triPosB = positions[facePosB];
            Vec3 triPosC = positions[facePosC];

            if (Vec3.Cross(triPosB - triPosA, triPosC - triPosA).MagnitudeSqr() < 1e-12f)
                continue; // Zero area triangle; skip.

            if (faceNormA >= 0 && faceNormB >= 0 && faceNormC >= 0)
                output.Add(new Triangle(triPosA, triPosB, triPosC, normals[faceNormA], normals[faceNormB], normals[faceNormC]));
            else
                output.Add(new Triangle(triPosA, triPosB, triPosC));
        }
    }

    private static void ReadFace(ObjLexer lexer, int posCount, int normCount, List<(int Pos, int Norm)> output)
    {
        while (lexer.Peek() == ObjToken.Word)
        {
            int pos = ReadIndex(lexer, posCount);
            int norm = -1;
            if (lexer.Peek() == ObjToken.Slash)
            {
                lexer.Next(); // First slash
                if (lexer.Peek() == ObjToken.Word)
                    lexer.Next(); // Skip texcoord index
                if (lexer.Peek() == ObjToken.Slash)
                {
                    lexer.Next(); // Second slash
                    if (lexer.Peek() == ObjToken.Word)
                        norm = ReadIndex(lexer, normCount);
                }
            }
            output.Add((pos, norm));
        }
    }

    private static int ReadIndex(ObjLexer lexer, int count)
    {
        // OBJ indices are 1-based; negative indices are relative to the end.
        int idx = ReadInt(lexer);
        idx = idx < 0 ? count + idx : idx - 1;
        if (idx < 0 || idx >= count)
            throw new Exception($"OBJ: index out of bounds ({idx}, count: {count})");
        return idx;
    }

    private static Vec3 ReadVec3(ObjLexer lexer) =>
        new Vec3(ReadFloat(lexer), ReadFloat(lexer), ReadFloat(lexer));

    private static Color ReadColor(ObjLexer lexer) =>
        new Color(ReadFloat(lexer), ReadFloat(lexer), ReadFloat(lexer));

    private static float ReadFloat(ObjLexer lexer)
    {
        Span<char> buf = stackalloc char[32];
        ObjToken kind = lexer.Next(buf, out int len);
        if (kind != ObjToken.Word)
            throw new Exception($"OBJ: expected number, got {kind}");
        if (!float.TryParse(buf[..len], NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            throw new Exception($"OBJ: invalid number '{buf[..len].ToString()}'");
        return val;
    }

    private static int ReadInt(ObjLexer lexer)
    {
        Span<char> buf = stackalloc char[16];
        ObjToken kind = lexer.Next(buf, out int len);
        if (kind != ObjToken.Word)
            throw new Exception($"OBJ: expected int, got {kind}");
        if (!int.TryParse(buf[..len], out int val))
            throw new Exception($"OBJ: invalid int '{buf[..len].ToString()}'");
        return val;
    }
}
