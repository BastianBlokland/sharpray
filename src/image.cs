using System;
using System.Diagnostics;
using System.IO;

struct Pixel
{
    public byte R, G, B;

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

class Image
{
    public uint Width { get; init; }
    public uint Height { get; init; }
    public Pixel[] Pixels { get; init; }

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
        if (imageType != 2)
            throw new Exception("TGA: Only uncompressed TrueColor images supported (no RLE)");
        if (bitsPerPixel != 24)
            throw new Exception($"TGA: Only 24-bit images supported, got {bitsPerPixel}-bit");

        uint pixelCount = width * height;
        Pixel[] pixels = new Pixel[pixelCount];
        for (uint i = 0; i != pixelCount; ++i)
        {
            byte b = reader.ReadByte();
            byte g = reader.ReadByte();
            byte r = reader.ReadByte();
            pixels[i] = new Pixel(r, g, b);
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
