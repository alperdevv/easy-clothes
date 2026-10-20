using System.Buffers.Binary;
using System.IO.Compression;

namespace EasyClothes.Core.Textures;

// Decodes the common PNG flavours: 8-bit grey, grey+alpha, RGB, RGBA and palette, not interlaced.
public static class PngReader
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static RgbaImage Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static RgbaImage Read(Stream input)
    {
        var reader = new BinaryReader(input);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual(Signature)) throw new InvalidDataException("No es un PNG.");

        int width = 0, height = 0, colourType = 0;
        byte[] palette = [], transparency = [];
        using var idat = new MemoryStream();
        while (true)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            var type = new string(reader.ReadChars(4));
            var data = reader.ReadBytes(length);
            reader.ReadBytes(4); // CRC
            if (data.Length < length) throw new InvalidDataException("PNG truncado.");

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                    colourType = data[9];
                    if (data[8] != 8 || data[12] != 0)
                        throw new NotSupportedException($"PNG de {data[8]} bits o entrelazado; guárdalo como RGBA de 8 bits.");
                    break;
                case "PLTE": palette = data; break;
                case "tRNS": transparency = data; break;
                case "IDAT": idat.Write(data); break;
                case "IEND": return Decode(width, height, colourType, idat, palette, transparency);
            }
        }
    }

    private static RgbaImage Decode(int width, int height, int colourType, MemoryStream idat, byte[] palette, byte[] transparency)
    {
        var channels = colourType switch
        {
            0 or 3 => 1,
            4 => 2,
            2 => 3,
            6 => 4,
            _ => throw new NotSupportedException($"Tipo de color PNG {colourType} no soportado."),
        };
        var stride = width * channels;
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new byte[stride];
        var previous = new byte[stride];
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var filter = z.ReadByte();
            z.ReadExactly(raw);
            Unfilter(filter, raw, previous, channels);
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                var s = x * channels;
                switch (colourType)
                {
                    case 0:
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = raw[s];
                        pixels[o + 3] = 255;
                        break;
                    case 4:
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = raw[s];
                        pixels[o + 3] = raw[s + 1];
                        break;
                    case 3:
                        var i = raw[s];
                        if (i * 3 + 2 >= palette.Length) throw new InvalidDataException("Índice de paleta fuera de rango.");
                        pixels[o] = palette[i * 3];
                        pixels[o + 1] = palette[i * 3 + 1];
                        pixels[o + 2] = palette[i * 3 + 2];
                        pixels[o + 3] = i < transparency.Length ? transparency[i] : (byte)255;
                        break;
                    default:
                        pixels[o] = raw[s];
                        pixels[o + 1] = raw[s + 1];
                        pixels[o + 2] = raw[s + 2];
                        pixels[o + 3] = channels == 4 ? raw[s + 3] : (byte)255;
                        break;
                }
            }
            (raw, previous) = (previous, raw);
        }
        return new RgbaImage(width, height, pixels);
    }

    private static void Unfilter(int filter, byte[] row, byte[] previous, int bpp)
    {
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= bpp ? row[i - bpp] : 0, up = previous[i], upLeft = i >= bpp ? previous[i - bpp] : 0;
            row[i] = (byte)(row[i] + filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"Filtro PNG {filter} desconocido."),
            });
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
