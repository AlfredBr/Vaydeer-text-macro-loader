namespace Vaydeer;

internal record KeyChord(byte[] Modifiers, byte Key);

internal static class NamedKeys
{
    private static readonly Dictionary<string, byte> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = 17, ["Control"] = 17, ["Alt"] = 18, ["Shift"] = 16,
        ["Win"] = 91, ["Windows"] = 91
    };
    private static readonly Dictionary<string, byte> Keys = CreateKeys();
    private static readonly Dictionary<byte, string> Names = Keys.GroupBy(pair => pair.Value)
        .ToDictionary(group => group.Key, group => group.First().Key);

    private static Dictionary<string, byte> CreateKeys()
    {
        var keys = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enter"] = 13, ["Tab"] = 9, ["Space"] = 32, ["Escape"] = 27,
            ["Backspace"] = 8, ["Delete"] = 46, ["Insert"] = 45,
            ["Home"] = 36, ["End"] = 35, ["PageUp"] = 33, ["PageDown"] = 34,
            ["Left"] = 37, ["Up"] = 38, ["Right"] = 39, ["Down"] = 40,
            ["Esc"] = 27, ["Return"] = 13, ["Del"] = 46, ["Ins"] = 45,
            ["Equals"] = 187, ["Minus"] = 189, ["Comma"] = 188, ["Period"] = 190,
            ["Slash"] = 191, ["Backslash"] = 220, ["Semicolon"] = 186,
            ["Quote"] = 222, ["LeftBracket"] = 219, ["RightBracket"] = 221, ["Backtick"] = 192
        };
        for (byte code = 65; code <= 90; code++) keys[((char)code).ToString()] = code;
        for (byte code = 48; code <= 57; code++) keys[((char)code).ToString()] = code;
        for (int number = 1; number <= 12; number++) keys[$"F{number}"] = (byte)(111 + number);
        return keys;
    }

    public static KeyChord Parse(string shortcut)
    {
        string[] parts = shortcut.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrEmpty)) throw new InvalidDataException("A press must name one key, optionally preceded by modifiers (for example Ctrl+Shift+S).");
        var modifiers = new List<byte>();
        foreach (string part in parts[..^1])
        {
            if (!Modifiers.TryGetValue(part, out byte code) || modifiers.Contains(code))
                throw new InvalidDataException($"Invalid or duplicate modifier: {part}.");
            modifiers.Add(code);
        }
        if (!Keys.TryGetValue(parts[^1], out byte key))
            throw new InvalidDataException($"Unsupported key: {parts[^1]}. Use A–Z, 0–9, F1–F12, or a documented named key.");
        return new(modifiers.ToArray(), key);
    }

    public static bool IsModifier(byte key) => key is 16 or 17 or 18 or 91;

    public static string Format(IEnumerable<byte> modifiers, byte key)
    {
        if (!Names.TryGetValue(key, out string? name)) throw new InvalidDataException($"Unsupported macro key code: {key}.");
        return string.Join('+', modifiers.Select(code => code switch
        {
            16 => "Shift", 17 => "Ctrl", 18 => "Alt", 91 => "Win",
            _ => throw new InvalidDataException("Unsupported modifier.")
        }).Append(name));
    }
}
