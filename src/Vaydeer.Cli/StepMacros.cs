using System.Text;

namespace Vaydeer;

internal static class StepMacros
{
    public static void Validate(KeyConfig config)
    {
        if (config.Enter || config.EnterDelayMs is not null)
            throw new InvalidDataException("steps cannot use enter or enterDelayMs; use press and delay steps.");
        if (config.Steps is not { Length: > 0 and <= 512 }) throw new InvalidDataException("steps must contain 1–512 actions.");
        bool hasAction = false, previousDelay = false;
        foreach (var step in config.Steps)
        {
            if (step is null || (step.Text is null ? 0 : 1) + (step.Press is null ? 0 : 1) + (step.DelayMs is null ? 0 : 1) != 1)
                throw new InvalidDataException("Each step must contain exactly one of text, press, or delayMs.");
            if (step.Press is not null)
            {
                NamedKeys.Parse(step.Press);
                hasAction = true; previousDelay = false;
            }
            else if (step.Text is { Length: > 0 }) { hasAction = true; previousDelay = false; }
            else if (step.DelayMs is int delay)
            {
                if (delay is < 0 or > 65535) throw new InvalidDataException("A delay step must be an integer from 0 to 65535 milliseconds.");
                if (!hasAction || previousDelay) throw new InvalidDataException("A delay step needs a preceding text or press action; leading and consecutive delays are not supported.");
                previousDelay = true;
            }
        }
    }

    public static KeyRecord Compile(KeyConfig config, Func<char, int> map)
    {
        var data = new List<byte>();
        bool explicitFinalDelay = false;
        void Event(byte code, bool release) => data.AddRange([code, (byte)(release ? 1 : 0), 5, 0]);
        void Gap(int delay) { data[^2] = (byte)(delay & 255); data[^1] = (byte)(delay >> 8); }
        foreach (var step in config.Steps!)
        {
            if (step.Text is string text)
            {
                if (text.Length == 0) continue;
                var encoded = Configuration.Compile(config with { Steps = null, Text = text, Enter = false }, map);
                data.AddRange(encoded.Data);
                Gap(config.DelayMs ?? 5);
                explicitFinalDelay = false;
            }
            else if (step.Press is string press)
            {
                var chord = NamedKeys.Parse(press);
                foreach (byte modifier in chord.Modifiers) Event(modifier, false);
                Event(chord.Key, false); Event(chord.Key, true);
                foreach (byte modifier in chord.Modifiers.Reverse()) Event(modifier, true);
                Gap(config.DelayMs ?? 5);
                explicitFinalDelay = false;
            }
            else { Gap(step.DelayMs!.Value); explicitFinalDelay = true; }
            if (data.Count > 2048) throw new InvalidDataException($"Key {config.Key}: steps exceed the 512 key-event limit.");
        }
        byte[] name = Encoding.BigEndianUnicode.GetBytes(config.Name);
        if (data.Count == 0) return new(config.Key, [255,0,255,0, .. name], [0]);
        if (!explicitFinalDelay) Gap(50);
        return new(config.Key, [255,5,0,0, .. name], data.ToArray());
    }

    public static KeyConfig Decode(KeyRecord record, string name)
    {
        byte[] data = record.Data;
        if (data.Length == 0 || data.Length % 4 != 0) throw new InvalidDataException("Invalid macro events.");
        int Interval(int offset) => data[offset + 2] | data[offset + 3] << 8;
        var steps = new List<MacroStep>();
        int offset = 0;
        while (offset < data.Length)
        {
            var modifiers = new List<byte>();
            while (offset < data.Length && NamedKeys.IsModifier(data[offset]) && data[offset + 1] == 0)
            {
                if (modifiers.Contains(data[offset]) || Interval(offset) != 5) throw new InvalidDataException("Unsupported modifier events or timing.");
                modifiers.Add(data[offset]); offset += 4;
            }
            if (offset + 8 > data.Length || NamedKeys.IsModifier(data[offset]) || data[offset + 1] != 0
                || data[offset + 4] != data[offset] || data[offset + 5] != 1 || Interval(offset) != 5)
                throw new InvalidDataException("Macro has unsupported overlapping or unmatched key events.");
            byte key = data[offset];
            offset += 4;
            foreach (byte modifier in modifiers.AsEnumerable().Reverse())
            {
                if (Interval(offset) != 5 || offset + 8 > data.Length || data[offset + 4] != modifier || data[offset + 5] != 1)
                    throw new InvalidDataException("Macro modifiers are not released in reverse order.");
                offset += 4;
            }
            int gap = Interval(offset);
            offset += 4;
            steps.Add(new MacroStep { Press = NamedKeys.Format(modifiers, key) });
            if (gap != (offset == data.Length ? 50 : 5)) steps.Add(new MacroStep { DelayMs = gap });
        }
        return new KeyConfig { Key = record.Key, Name = name, Steps = steps.ToArray() };
    }
}
