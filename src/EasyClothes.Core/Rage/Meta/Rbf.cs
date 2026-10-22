using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace EasyClothes.Core.Rage.Meta;

// The game's binary XML ("RBF0"), used by small .ymt files such as creature metadata.
// Leaf values are <name value="..."/> elements; typed arrays carry content="char_array" or "short_array".
public static class Rbf
{
    private const uint Magic = 0x30464252;
    private const byte Open = 0x00, Int = 0x10, True = 0x20, False = 0x30, Float = 0x40, Attribute = 0x60;
    private const byte Close = 0xFF, Bytes = 0xFD;

    public static byte[] Write(XElement root)
    {
        using var output = new MemoryStream();
        var w = new BinaryWriter(output);
        w.Write(Magic);
        WriteElement(w, root, []);
        return output.ToArray();
    }

    private static void WriteElement(BinaryWriter w, XElement element, Dictionary<string, int> names)
    {
        var name = element.Name.LocalName;
        if (!element.HasElements && element.Attributes().Count() == 1 && element.Attribute("value") is { } value)
        {
            var text = value.Value;
            if (text is "true" or "false") Descriptor(w, names, name, text == "true" ? True : False);
            else if (TryParseInt(text, out var number))
            {
                Descriptor(w, names, name, Int);
                w.Write(number);
            }
            else
            {
                Descriptor(w, names, name, Float);
                w.Write(float.Parse(text, CultureInfo.InvariantCulture));
            }
            return;
        }

        var attributes = element.Attributes().ToList();
        Descriptor(w, names, name, Open);
        w.Write((short)0);
        w.Write((short)0);
        w.Write((short)attributes.Count);
        foreach (var attribute in attributes)
        {
            Descriptor(w, names, attribute.Name.LocalName, Attribute);
            WriteString(w, attribute.Value);
        }
        foreach (var child in element.Elements()) WriteElement(w, child, names);
        if (element.Attribute("content")?.Value is { } content)
        {
            var data = ArrayBytes(content, element.Value);
            w.Write(Bytes);
            w.Write(Close);
            w.Write(data.Length);
            w.Write(data);
        }
        w.Write(Close);
        w.Write(Close);
    }

    private static void Descriptor(BinaryWriter w, Dictionary<string, int> names, string name, byte type)
    {
        if (names.TryGetValue(name, out var index))
        {
            w.Write((byte)index);
            w.Write(type);
            return;
        }
        index = names.Count;
        if (index >= Bytes) throw new InvalidDataException("Too many distinct names for an RBF file.");
        names[name] = index;
        w.Write((byte)index);
        w.Write(type);
        WriteString(w, name);
    }

    private static void WriteString(BinaryWriter w, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        w.Write((short)bytes.Length);
        w.Write(bytes);
    }

    private static byte[] ArrayBytes(string content, string text)
    {
        var values = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return content switch
        {
            "char_array" => values.Select(v => (byte)v).ToArray(),
            "short_array" => values.SelectMany(v => BitConverter.GetBytes((short)v)).ToArray(),
            _ => throw new NotSupportedException($"RBF array content {content}."),
        };
    }

    private static bool TryParseInt(string text, out int value) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static XElement Read(byte[] data)
    {
        if (data.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            throw new InvalidDataException("Not an RBF file.");
        var names = new List<string>();
        var stack = new Stack<XElement>();
        XElement? root = null;
        var p = 4;
        while (p < data.Length)
        {
            var index = data[p];
            var type = data[p + 1];
            p += 2;
            if (index == Close)
            {
                var closed = stack.Pop();
                if (stack.Count == 0) root = closed;
                continue;
            }
            if (index == Bytes)
            {
                var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p));
                stack.Peek().Value = ArrayText(stack.Peek().Attribute("content")?.Value, data[(p + 4)..(p + 4 + length)]);
                p += 4 + length;
                continue;
            }
            if (index == names.Count) names.Add(ReadString(data, ref p));
            var name = names[index];
            switch (type)
            {
                case Open:
                    var element = new XElement(name);
                    if (stack.Count > 0) stack.Peek().Add(element);
                    stack.Push(element);
                    p += 6;
                    break;
                case Attribute:
                    stack.Peek().SetAttributeValue(name, ReadString(data, ref p));
                    break;
                case Int:
                    stack.Peek().Add(new XElement(name, new XAttribute("value", BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p)))));
                    p += 4;
                    break;
                case Float:
                    stack.Peek().Add(new XElement(name, new XAttribute("value", BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(p)).ToString(CultureInfo.InvariantCulture))));
                    p += 4;
                    break;
                case True or False:
                    stack.Peek().Add(new XElement(name, new XAttribute("value", type == True ? "true" : "false")));
                    break;
                default:
                    throw new InvalidDataException($"Unknown RBF value type 0x{type:X2} at {p - 2}.");
            }
        }
        return root ?? throw new InvalidDataException("RBF file has no root element.");
    }

    private static string ReadString(byte[] data, ref int p)
    {
        var length = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(p));
        var text = Encoding.ASCII.GetString(data, p + 2, length);
        p += 2 + length;
        return text;
    }

    private static string ArrayText(string? content, byte[] bytes) => content switch
    {
        "char_array" => string.Join(' ', bytes),
        "short_array" => string.Join(' ', Enumerable.Range(0, bytes.Length / 2).Select(i => BitConverter.ToInt16(bytes, i * 2))),
        _ => throw new NotSupportedException($"RBF array content {content}."),
    };
}
