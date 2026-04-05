using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

readonly struct Pixel
{
    public readonly byte R, G, B;

    public Pixel(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public Pixel(byte v)
    {
        R = v;
        G = v;
        B = v;
    }

    public static Pixel Black => new Pixel(0);
    public static Pixel White => new Pixel(255);
    public static Pixel Red => new Pixel(255, 0, 0);
    public static Pixel Green => new Pixel(0, 255, 0);
    public static Pixel Blue => new Pixel(0, 0, 255);
}

readonly struct PixelHdr
{
    public readonly byte R, G, B, E; // E = Exponent.

    public PixelHdr(byte r, byte g, byte b, byte e)
    {
        R = r;
        G = g;
        B = b;
        E = e;
    }
}

class Image
{
    public uint Width { get; init; }
    public uint Height { get; init; }
    public Pixel[] Pixels { get; init; }

    public Vec2i Size => new Vec2i((int)Width, (int)Height);

    public Image(uint width, uint height)
    {
        Debug.Assert(width > 0 && height > 0);
        Width = width;
        Height = height;
        Pixels = new Pixel[width * height];
    }

    private Image(uint width, uint height, Pixel[] pixels)
    {
        Debug.Assert(width > 0 && height > 0);
        Debug.Assert(pixels.Length == width * height);
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public void FlipVertical()
    {
        for (uint y = 0; y != Height / 2; ++y)
        {
            uint topRow = y * Width;
            uint bottomRow = (Height - 1 - y) * Width;
            for (uint x = 0; x != Width; ++x)
                (Pixels[topRow + x], Pixels[bottomRow + x]) = (Pixels[bottomRow + x], Pixels[topRow + x]);
        }
    }

    public enum Format
    {
        Tga,
        Ppm,
        Bmp,
    }

    public bool Write(BinaryWriter writer, Format format) => format switch
    {
        Format.Tga => WriteTga(writer),
        Format.Ppm => WritePpm(writer),
        Format.Bmp => WriteBmp(writer),
        _ => false,
    };

    public bool WriteTga(BinaryWriter writer)
    {
        try
        {
            // Header.
            writer.Write((byte)0); // idLength.
            writer.Write((byte)0); // colorMapType.
            writer.Write((byte)2); // imageType: TrueColor.
            writer.Write(new byte[9]); // colorMapSpec and origin.
            writer.Write((ushort)Width); // image width (little-endian).
            writer.Write((ushort)Height); // image height (little-endian).
            writer.Write((byte)24); // bitsPerPixel.
            writer.Write((byte)0x20); // imageSpecDescriptor: top-left origin.

            // Pixels.
            foreach (Pixel pixel in Pixels)
            {
                writer.Write(pixel.B);
                writer.Write(pixel.G);
                writer.Write(pixel.R);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool WritePpm(BinaryWriter writer)
    {
        try
        {
            // Header.
            string header = $"P6\n{Width} {Height}\n255\n";
            writer.Write(System.Text.Encoding.ASCII.GetBytes(header));

            // Pixels.
            foreach (Pixel pixel in Pixels)
            {
                writer.Write(pixel.R);
                writer.Write(pixel.G);
                writer.Write(pixel.B);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool WriteBmp(BinaryWriter writer)
    {
        try
        {
            uint rowStride = (Width * 3 + 3) & ~3u; // Rows padded to 4-byte boundary.
            uint pixelDataSize = rowStride * Height;
            uint fileSize = 14 + 40 + pixelDataSize; // File header + DIB header + pixels.

            // File header.
            writer.Write((byte)'B'); // signature.
            writer.Write((byte)'M'); // signature.
            writer.Write(fileSize); // file size.
            writer.Write((uint)0); // reserved.
            writer.Write((uint)54); // pixel data offset (14 + 40).

            // DIB header.
            writer.Write((uint)40); // header size.
            writer.Write((int)Width); // image width.
            writer.Write(-(int)Height); // negative height: top-down row order.
            writer.Write((ushort)1); // color planes.
            writer.Write((ushort)24); // bits per pixel.
            writer.Write((uint)0); // compression: none.
            writer.Write(pixelDataSize); // image size.
            writer.Write(new byte[16]); // x/y pixels per meter, colors in table, important colors.

            // Pixels.
            uint padding = rowStride - Width * 3;
            for (uint y = 0; y != Height; ++y)
            {
                for (uint x = 0; x != Width; ++x)
                {
                    Pixel pixel = Pixels[y * Width + x];
                    writer.Write(pixel.B);
                    writer.Write(pixel.G);
                    writer.Write(pixel.R);
                }
                writer.Write(new byte[padding]);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Save(string path)
    {
        Format? format = FormatFromPath(path);
        if (format == null)
        {
            return false;
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);
            return Write(writer, format.Value);
        }
        catch
        {
            return false;
        }
    }

    public static Image Load(string path)
    {
        Format? format = FormatFromPath(path);
        if (format == null)
            throw new Exception($"Image: Unsupported file format '{Path.GetExtension(path)}'");
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        return format switch
        {
            Format.Tga => LoadTga(reader),
            _ => throw new Exception($"Image: Loading not supported for format '{format}'"),
        };
    }

    private static Image LoadTga(BinaryReader reader)
    {
        // TGA header (18 bytes).
        // Format reference: http://www.paulbourke.net/dataformats/tga/
        reader.ReadByte(); // idLength (skip).
        byte colorMapType = reader.ReadByte();
        byte imageType = reader.ReadByte();
        reader.ReadBytes(5); // colorMapSpec (skip).
        reader.ReadBytes(4); // xOrigin + yOrigin (skip).
        uint width = reader.ReadUInt16();
        uint height = reader.ReadUInt16();
        byte bitsPerPixel = reader.ReadByte();
        byte descriptor = reader.ReadByte();

        if (colorMapType != 0)
            throw new Exception("TGA: Color-mapped images not supported");
        if (imageType != 2 && imageType != 3)
            throw new Exception($"TGA: Unsupported image type {imageType} (only uncompressed TrueColor and Grayscale supported)");
        if (imageType == 2 && bitsPerPixel != 24)
            throw new Exception($"TGA: Only 24-bit TrueColor images supported, got {bitsPerPixel}-bit");
        if (imageType == 3 && bitsPerPixel != 8)
            throw new Exception($"TGA: Only 8-bit Grayscale images supported, got {bitsPerPixel}-bit");

        uint pixelCount = width * height;
        Pixel[] pixels = new Pixel[pixelCount];
        if (imageType == 3)
        {
            for (uint i = 0; i != pixelCount; ++i)
            {
                pixels[i] = new Pixel(reader.ReadByte());
            }
        }
        else
        {
            for (uint i = 0; i != pixelCount; ++i)
            {
                byte b = reader.ReadByte();
                byte g = reader.ReadByte();
                byte r = reader.ReadByte();
                pixels[i] = new Pixel(r, g, b);
            }
        }

        Image image = new Image(width, height, pixels);
        if ((descriptor & 0x20) == 0)
            image.FlipVertical();
        return image;
    }

    public static Format? FormatFromPath(string path)
    {
        string ext = Path.GetExtension(path);
        if (ext == ".tga") return Format.Tga;
        if (ext == ".ppm") return Format.Ppm;
        if (ext == ".bmp") return Format.Bmp;
        return null;
    }
}

class ImageHdr
{
    public uint Width { get; init; }
    public uint Height { get; init; }
    public PixelHdr[] Pixels { get; init; }

    public Vec2i Size => new Vec2i((int)Width, (int)Height);

    private ImageHdr(uint width, uint height, PixelHdr[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static ImageHdr Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        return LoadHdr(reader);
    }

    private static ImageHdr LoadHdr(BinaryReader reader)
    {
        // HDR image loader for the Radiance HDR (.hdr) format.
        // https://paulbourke.net/dataformats/pic/

        Span<char> buffer = stackalloc char[256];

        ReadOnlySpan<char> magic = ReadLine(reader, buffer);
        if (!magic.StartsWith("#?RADIANCE") && !magic.StartsWith("#?RGBE"))
            throw new Exception($"ImageHdr: Invalid magic '{magic}'");

        // Skip metadata lines until empty line.
        while (ReadLine(reader, buffer).Length > 0)
            ;

        ReadOnlySpan<char> sizeLine = ReadLine(reader, buffer);
        Span<Range> sizeParts = stackalloc Range[4];
        if (sizeLine.Split(sizeParts, ' ') != 4 || !sizeLine[sizeParts[0]].SequenceEqual("-Y") || !sizeLine[sizeParts[2]].SequenceEqual("+X"))
            throw new Exception($"ImageHdr: Unsupported size/orientation '{sizeLine}'");

        uint height = uint.Parse(sizeLine[sizeParts[1]]);
        uint width = uint.Parse(sizeLine[sizeParts[3]]);

        PixelHdr[] pixels = new PixelHdr[width * height];

        Span<byte> scanline = stackalloc byte[(int)width * 4]; // Interleaved RGBE per pixel.
        Span<byte> scanlineHeader = stackalloc byte[4];
        for (uint y = 0; y != height; ++y)
        {
            reader.Read(scanlineHeader);

            if (scanlineHeader[0] == 2 && scanlineHeader[1] == 2 && (scanlineHeader[2] & 0x80) == 0)
            {
                // RLE encoding.
                uint lineWidth = ((uint)scanlineHeader[2] << 8) | scanlineHeader[3];
                if (lineWidth != width)
                    throw new Exception($"ImageHdr: Scanline width mismatch ({lineWidth} vs {width})");

                for (int channel = 0; channel != 4; ++channel)
                {
                    for (int x = 0; x < width;)
                    {
                        byte code = reader.ReadByte();
                        if (code > 128)
                        {
                            // Run: repeat next byte (code - 128) times.
                            int count = code - 128;
                            byte val = reader.ReadByte();
                            for (int i = 0; i != count; ++i)
                                scanline[x++ * 4 + channel] = val;
                        }
                        else
                        {
                            // Non-run: read code literal bytes.
                            for (int i = 0; i != code; ++i)
                                scanline[x++ * 4 + channel] = reader.ReadByte();
                        }
                    }
                }
            }
            else
            {
                if (scanlineHeader[0] == 1 && scanlineHeader[1] == 1 && scanlineHeader[2] == 1)
                    throw new Exception("ImageHdr: Old RLE format not supported");

                // Uncompressed scanline (header contains the first pixel).
                scanlineHeader.CopyTo(scanline);
                reader.Read(scanline[4..]);
            }

            // Output the scanline.
            MemoryMarshal.Cast<byte, PixelHdr>(scanline).CopyTo(pixels.AsSpan((int)(y * width), (int)width));
        }

        return new ImageHdr(width, height, pixels);
    }

    private static ReadOnlySpan<char> ReadLine(BinaryReader reader, Span<char> buffer)
    {
        int length = 0;
        while (true)
        {
            byte val = reader.ReadByte();
            switch (val)
            {
                case (byte)'\n': return buffer[..length];
                case (byte)'\r': break;
                default: buffer[length++] = (char)val; break;
            }
        }
    }
}
