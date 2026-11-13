using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vaydeer;

internal sealed record KeyConfig
{
    [JsonRequired] public int Key { get; init; }
    [JsonRequired] public string Name { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Text { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MacroStep[]? Steps { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Enter { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DelayMs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EnterDelayMs { get; init; }
    [JsonIgnore] public int CharacterCount => Text?.Length ?? Steps?.Sum(step => step.Text?.Length ?? 0) ?? 0;
}

internal sealed record MacroStep
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Press { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? DelayMs { get; init; }
}

internal static class Configuration
{
    public const int MaxNameLength = 16;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static KeyConfig[] Read(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Configuration file exceeds 1 MiB.");
        return Parse(File.ReadAllText(path));
    }

    public static KeyConfig[] Parse(string json)
    {
        // System.Text.Json otherwise accepts duplicate property names, hiding typos.
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Configuration must be a JSON array of keys.");
        foreach (var element in document.RootElement.EnumerateArray())
        {
            CheckProperties(element);
            bool text = element.TryGetProperty("text", out var textValue);
            bool steps = element.TryGetProperty("steps", out var stepsValue);
            if (text == steps) throw new InvalidDataException("Each key must specify exactly one of text or steps.");
            if (text && textValue.ValueKind != JsonValueKind.String) throw new InvalidDataException("text must be a string.");
            if (steps)
            {
                if (stepsValue.ValueKind != JsonValueKind.Array) throw new InvalidDataException("steps must be an array.");
                if (element.TryGetProperty("enter", out _) || element.TryGetProperty("enterDelayMs", out _))
                    throw new InvalidDataException("Use press: Enter and delay steps instead of enter/enterDelayMs with steps.");
                foreach (var step in stepsValue.EnumerateArray()) CheckProperties(step);
            }
        }
        var keys = JsonSerializer.Deserialize<KeyConfig[]>(json, Json) ?? throw new InvalidDataException("Missing keys.");
        if (keys.Length is < 1 or > 9) throw new InvalidDataException("Specify one to nine keys.");
        var numbers = new HashSet<int>();
        foreach (var key in keys)
        {
            if (key.Key is < 1 or > 9 || !numbers.Add(key.Key)) throw new InvalidDataException("Key numbers must be unique integers from 1 to 9.");
            ValidateName(key);
            if ((key.Text is null) == (key.Steps is null)) throw new InvalidDataException($"Key {key.Key}: specify exactly one of text or steps.");
            if (key.Steps is not null) StepMacros.Validate(key);
            ValidateTiming(key);
        }
        return keys.OrderBy(key => key.Key).ToArray();
    }

    public static KeyRecord Compile(KeyConfig config, Func<char, int> map)
    {
        ValidateName(config);
        ValidateTiming(config);
        if (config.Steps is not null)
        {
            if (config.Text is not null) throw new InvalidDataException("Specify text or steps, not both.");
            StepMacros.Validate(config);
            return StepMacros.Compile(config, map);
        }
        if (config.Text is null) throw new InvalidDataException("text or steps is required.");
        var data = new List<byte>();
        // Timing matches the vendor-created simple macros read from this keypad.
        void Event(byte code, bool release) => data.AddRange([code, (byte)(release ? 1 : 0), 5, 0]);
        void Press(byte code) { Event(code, false); Event(code, true); }
        void SetGap(int milliseconds)
        {
            data[^2] = (byte)(milliseconds & 255);
            data[^1] = (byte)(milliseconds >> 8);
        }
        for (int i = 0; i < config.Text.Length; i++)
        {
            char ch = config.Text[i];
            if (ch is '\r' or '\n')
            {
                Press(13);
                if (ch == '\r' && i + 1 < config.Text.Length && config.Text[i + 1] == '\n') i++;
            }
            else if (ch == '\t') Press(9);
            else
            {
                if (char.IsControl(ch) || char.IsSurrogate(ch))
                    throw new InvalidDataException($"Key {config.Key}: unsupported character U+{(int)ch:X4} at position {i + 1}.");
                int mapping = map(ch);
                if (mapping < 0 || (mapping >> 8 & ~7) != 0 || (mapping & 255) == 0)
                    throw new InvalidDataException($"Key {config.Key}: U+{(int)ch:X4} cannot be typed with the current keyboard layout.");
                byte key = (byte)(mapping & 255);
                // Stock firmware does not implement all Windows virtual keys.
                if (!SupportedKey(key)) throw new InvalidDataException($"Key {config.Key}: character U+{(int)ch:X4} requires an unsupported key.");
                var modifiers = new List<byte>();
                if ((mapping & 0x100) != 0) modifiers.Add(16);
                if ((mapping & 0x200) != 0) modifiers.Add(17);
                if ((mapping & 0x400) != 0) modifiers.Add(18);
                foreach (byte modifier in modifiers) Event(modifier, false);
                Press(key);
                foreach (byte modifier in modifiers.AsEnumerable().Reverse()) Event(modifier, true);
            }
            // Wait after the complete character, including any modifier releases.
            SetGap(config.DelayMs ?? 5);
            if (data.Count > 2048) throw new InvalidDataException($"Key {config.Key}: macro exceeds the CLI limit of 512 key events. Shorten the text.");
        }
        if (config.Enter)
        {
            if (data.Count != 0) SetGap(config.EnterDelayMs ?? config.DelayMs ?? 5);
            Press(13);
        }
        if (data.Count > 2048) throw new InvalidDataException($"Key {config.Key}: macro exceeds 512 key events including Enter.");
        byte[] name = Encoding.BigEndianUnicode.GetBytes(config.Name);
        if (data.Count == 0) return new(config.Key, [255, 0, 255, 0, .. name], [0]);
        // Last release gets a short settling interval; execute once per press.
        data[^2] = 50;
        data[^1] = 0;
        return new(config.Key, [255, 5, 0, 0, .. name], data.ToArray());
    }

    private static void ValidateTiming(KeyConfig config)
    {
        if (config.DelayMs is < 0 or > 65535 || config.EnterDelayMs is < 0 or > 65535)
            throw new InvalidDataException($"Key {config.Key}: delayMs and enterDelayMs must be integers from 0 to 65535 milliseconds.");
    }

    private static void ValidateName(KeyConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name) || config.Name.Length > MaxNameLength || config.Name.Any(char.IsControl))
            throw new InvalidDataException($"Key {config.Key}: name must contain 1–{MaxNameLength} UTF-16 units and no control characters. The stock firmware discards longer key labels.");
        if (config.Name.Any(char.IsSurrogate)) throw new InvalidDataException($"Key {config.Key}: supplementary characters in names are not supported.");
    }

    private static void CheckProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Keys and steps must be JSON objects.");
        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!properties.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property: {property.Name}.");
            if (property.Name is "delayMs" or "enterDelayMs" && property.Value.ValueKind != JsonValueKind.Number)
                throw new InvalidDataException($"{property.Name} must be an integer from 0 to 65535.");
            if (property.Name is "text" or "press" && property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{property.Name} must be a string.");
        }
    }

    private static bool SupportedKey(byte code) => code is >= 48 and <= 57 or >= 65 and <= 90
        or 32 or >= 186 and <= 192 or >= 219 and <= 222;

    public static Func<char, int> WindowsLayoutMapper()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This CLI requires Windows.");
        nint layout = GetKeyboardLayout(0);
        return ch =>
        {
            int mapping = VkKeyScanExW(ch, layout);
            if (mapping < 0) return -1;
            return Translate((byte)(mapping & 255), mapping >> 8, layout) == ch ? mapping : -1;
        };
    }

    public static Func<byte, int, char?> WindowsLayoutDecoder()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This CLI requires Windows.");
        nint layout = GetKeyboardLayout(0);
        return (key, modifiers) =>
        {
            char? ch = SupportedKey(key) ? Translate(key, modifiers, layout) : null;
            // Reject menu shortcuts such as Alt+A, even if Windows translates them to a letter.
            return ch is char value && !char.IsControl(value) && !char.IsSurrogate(value)
                && VkKeyScanExW(value, layout) == (key | modifiers << 8) ? ch : null;
        };
    }

    private static char? Translate(byte key, int modifiers, nint layout)
    {
        var state = new byte[256];
        if ((modifiers & 1) != 0) state[16] = 0x80;
        if ((modifiers & 2) != 0) state[17] = 0x80;
        if ((modifiers & 4) != 0) state[18] = 0x80;
        var output = new StringBuilder(8);
        // Flag 4 keeps this check from modifying the keyboard's dead-key state.
        int length = ToUnicodeEx(key, MapVirtualKeyExW(key, 0, layout), state, output, output.Capacity, 4, layout);
        return length == 1 ? output[0] : null;
    }

    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanExW(char ch, nint layout);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyExW(uint code, uint type, nint layout);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder output, int size, uint flags, nint layout);
}
