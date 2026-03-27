using System;
using System.IO;

Image img = new Image(128, 128);
Array.Fill(img.Pixels, new Pixel(255, 0, 0));
img.Save("image.bmp");

Console.WriteLine("Pew pew!");

struct Pixel
{
    public byte R, G, B;

    public Pixel(byte r, byte g, byte b)
    {
        this.R = r;
        this.G = g;
        this.B = b;
    }
}

class Image
{
    public uint Width { get; init; }
    public uint Height { get; init; }
    public Pixel[] Pixels { get; init; }

    public Image(uint width, uint height)
    {
        this.Width = width;
        this.Height = height;
        this.Pixels = new Pixel[width * height];
    }

    public enum Format
    {
        Tga,
        Ppm,
        Bmp,
    }

    static public Format? FormatFromPath(string path)
    {
        string ext = Path.GetExtension(path);
        if (ext == ".tga") return Format.Tga;
        if (ext == ".ppm") return Format.Ppm;
        if (ext == ".bmp") return Format.Bmp;
        return null;
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
            writer.Write((ushort)this.Width); // image width (little-endian).
            writer.Write((ushort)this.Height); // image height (little-endian).
            writer.Write((byte)24); // bitsPerPixel.
            writer.Write((byte)0x20); // imageSpecDescriptor: top-left origin.

            // Pixels.
            foreach (Pixel pixel in this.Pixels)
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
            string header = $"P6\n{this.Width} {this.Height}\n255\n";
            writer.Write(System.Text.Encoding.ASCII.GetBytes(header));

            // Pixels.
            foreach (Pixel pixel in this.Pixels)
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
            uint rowStride = (this.Width * 3 + 3) & ~3u; // Rows padded to 4-byte boundary.
            uint pixelDataSize = rowStride * this.Height;
            uint fileSize = 14 + 40 + pixelDataSize; // File header + DIB header + pixels.

            // File header.
            writer.Write((byte)'B'); // signature.
            writer.Write((byte)'M'); // signature.
            writer.Write(fileSize); // file size.
            writer.Write((uint)0); // reserved.
            writer.Write((uint)54); // pixel data offset (14 + 40).

            // DIB header.
            writer.Write((uint)40); // header size.
            writer.Write((int)this.Width); // image width.
            writer.Write(-(int)this.Height); // negative height: top-down row order.
            writer.Write((ushort)1); // color planes.
            writer.Write((ushort)24); // bits per pixel.
            writer.Write((uint)0); // compression: none.
            writer.Write(pixelDataSize); // image size.
            writer.Write(new byte[16]); // x/y pixels per meter, colors in table, important colors.

            // Pixels.
            uint padding = rowStride - this.Width * 3;
            for (uint y = 0; y != this.Height; ++y)
            {
                for (uint x = 0; x != this.Width; ++x)
                {
                    Pixel pixel = this.Pixels[y * this.Width + x];
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
}
