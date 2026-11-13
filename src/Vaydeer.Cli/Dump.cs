using System.Text;
using System.Text.Json;

namespace Vaydeer;

internal sealed record RawDumpKey(int Key, string Name, string Type, string Reason, string HeaderHex, string DataHex);

internal static class Dump
{
    private static readonly Encoding StrictUnicode = new UnicodeEncoding(true, false, true);

    public static object[] Read(Protocol keypad, Func<byte, int, char?> decode, Func<char, int> map)
    {
        keypad.Info();
        return Enumerable.Range(0, 9).Select(index => Convert(keypad.ReadKey(0, index), decode, map)).ToArray();
    }

    public static object Convert(KeyRecord key, Func<byte, int, char?> decode, Func<char, int> map)
    {
        string name = Encoding.BigEndianUnicode.GetString(key.Header.AsSpan(4)).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(name)) name = $"Key {key.Key}";
        try
        {
            string text = key.Header[1] switch
            {
                0 or 1 => DecodeKeys(key.Data, decode),
                2 when key.Header[2] == 0 && key.Header[3] == 0 => StrictUnicode.GetString(key.Data),
                5 when key.Header[2] == 0 && key.Header[3] == 0 => DecodeMacro(key.Data, decode),
                _ => throw new InvalidDataException("This factory action is not a simple text macro.")
            };
            bool enter = text.EndsWith('\n') || text.EndsWith('\r');
            if (enter) text = text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text[..^1];
            var config = new KeyConfig { Key = key.Key, Name = name, Text = text, Enter = enter };
            // Only label an entry as loadable text if the loader really accepts it.
            Configuration.Parse(JsonSerializer.Serialize(new[] { config }, Configuration.Json));
            Configuration.Compile(config, map);
            return config;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or JsonException)
        {
            string type = key.Header[1] switch
            {
                0 => "key", 1 => "shortcut", 2 => "text", 3 => "launch", 4 => "mouse",
                5 => "macro", 6 => "layer", _ => "unknown"
            };
            return new RawDumpKey(key.Key, name, type, error.Message,
                System.Convert.ToHexString(key.Header), System.Convert.ToHexString(key.Data));
        }
    }

    private static string DecodeKeys(byte[] codes, Func<byte, int, char?> decode)
    {
        if (codes.Length == 1 && codes[0] == 0) return "";
        int modifiers = 0;
        byte? main = null;
        foreach (byte code in codes)
        {
            int modifier = Modifier(code);
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0) throw new InvalidDataException("Repeated modifier is not a text shortcut.");
                modifiers |= modifier;
            }
            else
            {
                if (main is not null) throw new InvalidDataException("Multiple simultaneous keys cannot be represented as text.");
                main = code;
            }
        }
        if (main is null) throw new InvalidDataException("A modifier-only action is not text.");
        return Character(main.Value, modifiers, decode).ToString();
    }

    private static string DecodeMacro(byte[] data, Func<byte, int, char?> decode)
    {
        if (data.Length == 0 || data.Length % 4 != 0) throw new InvalidDataException("Macro has invalid or empty event data.");
        int modifiers = 0;
        var text = new StringBuilder();
        for (int offset = 0; offset < data.Length; offset += 4)
        {
            byte code = data[offset], action = data[offset + 1];
            if (action > 1) throw new InvalidDataException("Unknown macro event action.");
            int modifier = Modifier(code);
            if (modifier != 0)
            {
                bool held = (modifiers & modifier) != 0;
                if ((action == 0) == held) throw new InvalidDataException("Unbalanced modifier events.");
                modifiers = action == 0 ? modifiers | modifier : modifiers & ~modifier;
                continue;
            }
            if (action != 0 || offset + 8 > data.Length || data[offset + 4] != code || data[offset + 5] != 1)
                throw new InvalidDataException("Overlapping or unmatched key events cannot be represented as text.");
            text.Append(Character(code, modifiers, decode));
            offset += 4;
        }
        if (modifiers != 0 || text.Length == 0) throw new InvalidDataException("Macro does not contain balanced text key presses.");
        return text.ToString();
    }

    private static int Modifier(byte code) => code switch { 16 => 1, 17 => 2, 18 => 4, _ => 0 };
    private static char Character(byte code, int modifiers, Func<byte, int, char?> decode)
    {
        if (modifiers == 0 && code == 13) return '\n';
        if (modifiers == 0 && code == 9) return '\t';
        return decode(code, modifiers) ?? throw new InvalidDataException("Macro contains a shortcut or key that is not text in the current keyboard layout.");
    }
}
