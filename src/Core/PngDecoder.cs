using System.Buffers.Binary;
using System.IO.Compression;

namespace TopoCursor;

/// <summary>Immagine 32 bit: pixel ARGB non premoltiplicati, riga per riga dall'alto.</summary>
public sealed class Image32
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required uint[] Pixels { get; init; }
}

/// <summary>
/// Decoder PNG minimale e senza dipendenze (System.Drawing su Linux non esiste).
/// Supporta tutti i tipi colore (grigio, RGB, palette, grigio+alpha, RGBA), profondità 1–16 bit,
/// trasparenza tRNS. Non supporta i PNG interlacciati (Adam7), rari per gli spritesheet.
/// </summary>
public static class PngDecoder
{
    static ReadOnlySpan<byte> Signature => new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static Image32 Decode(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Il file non è un PNG valido.");

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();

        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (len < 0 || pos + 12 + len > png.Length) throw new InvalidDataException("PNG troncato.");
            var data = png.AsSpan(pos + 8, len);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    bitDepth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                    break;
                case "PLTE": palette = data.ToArray(); break;
                case "tRNS": trns = data.ToArray(); break;
                case "IDAT": idat.Write(data); break;
            }
            pos += 12 + len;
            if (type == "IEND") break;
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG senza intestazione IHDR valida.");
        if (interlace != 0)
            throw new InvalidDataException("PNG interlacciato non supportato: riesporta lo spritesheet senza interlacciamento.");

        int channels = colorType switch
        {
            0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4,
            _ => throw new InvalidDataException($"Tipo colore PNG {colorType} non valido."),
        };
        if (colorType == 3 && palette == null) throw new InvalidDataException("PNG a palette senza PLTE.");

        int bitsPerPixel = channels * bitDepth;
        int bpp = Math.Max(1, bitsPerPixel / 8);          // byte per pixel ai fini dei filtri
        int stride = (width * bitsPerPixel + 7) / 8;

        // decompressione zlib
        var raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n == 0) throw new InvalidDataException("Dati PNG incompleti.");
                read += n;
            }
        }

        // rimozione dei filtri, in place
        var prev = new byte[stride];
        var cur = new byte[stride];
        var pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (stride + 1);
            byte filter = raw[rowStart];
            Buffer.BlockCopy(raw, rowStart + 1, cur, 0, stride);
            Unfilter(filter, cur, prev, bpp);
            ConvertRow(cur, pixels.AsSpan(y * width, width), colorType, bitDepth, palette, trns);
            (prev, cur) = (cur, prev);
        }

        return new Image32 { Width = width, Height = height, Pixels = pixels };
    }

    static void Unfilter(byte filter, byte[] cur, byte[] prev, int bpp)
    {
        int n = cur.Length;
        switch (filter)
        {
            case 0: break;
            case 1: for (int i = bpp; i < n; i++) cur[i] += cur[i - bpp]; break;
            case 2: for (int i = 0; i < n; i++) cur[i] += prev[i]; break;
            case 3:
                for (int i = 0; i < n; i++)
                    cur[i] += (byte)(((i >= bpp ? cur[i - bpp] : 0) + prev[i]) >> 1);
                break;
            case 4:
                for (int i = 0; i < n; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    cur[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                }
                break;
            default: throw new InvalidDataException($"Filtro PNG {filter} non valido.");
        }
    }

    static void ConvertRow(byte[] row, Span<uint> dst, int colorType, int depth, byte[]? palette, byte[]? trns)
    {
        int w = dst.Length;
        // campione n-esimo della riga, ridotto a 8 bit (per 16 bit prende il byte alto)
        int Sample(int index)
        {
            switch (depth)
            {
                case 8: return row[index];
                case 16: return row[index * 2];
                default:
                    int bit = index * depth;
                    int v = (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                    return v;
            }
        }
        // per il confronto con tRNS servono i valori a piena profondità
        int Full(int index) => depth == 16 ? (row[index * 2] << 8) | row[index * 2 + 1] : Sample(index);
        int Scale(int v) => depth >= 8 ? v : v * 255 / ((1 << depth) - 1);

        for (int x = 0; x < w; x++)
        {
            uint a = 255, r, g, b;
            switch (colorType)
            {
                case 0:
                    {
                        int v = Sample(x);
                        r = g = b = (uint)Scale(v);
                        if (trns is { Length: >= 2 } && Full(x) == ((trns[0] << 8) | trns[1])) a = 0;
                        break;
                    }
                case 2:
                    r = (uint)Sample(x * 3); g = (uint)Sample(x * 3 + 1); b = (uint)Sample(x * 3 + 2);
                    if (trns is { Length: >= 6 } &&
                        Full(x * 3) == ((trns[0] << 8) | trns[1]) &&
                        Full(x * 3 + 1) == ((trns[2] << 8) | trns[3]) &&
                        Full(x * 3 + 2) == ((trns[4] << 8) | trns[5])) a = 0;
                    break;
                case 3:
                    {
                        int i = Sample(x);
                        if (i * 3 + 2 >= palette!.Length) { r = g = b = 0; a = 0; break; }
                        r = palette[i * 3]; g = palette[i * 3 + 1]; b = palette[i * 3 + 2];
                        if (trns != null && i < trns.Length) a = trns[i];
                        break;
                    }
                case 4:
                    r = g = b = (uint)Sample(x * 2); a = (uint)Sample(x * 2 + 1);
                    break;
                default: // 6
                    r = (uint)Sample(x * 4); g = (uint)Sample(x * 4 + 1); b = (uint)Sample(x * 4 + 2); a = (uint)Sample(x * 4 + 3);
                    break;
            }
            dst[x] = (a << 24) | (r << 16) | (g << 8) | b;
        }
    }
}
