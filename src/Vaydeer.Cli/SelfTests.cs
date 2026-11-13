using System.Text;

namespace Vaydeer;

internal static class SelfTests
{
    private static int count;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
        count++;
    }

    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException)
        { count++; return; }
        throw new InvalidOperationException($"Test failed: {name} should reject input");
    }

    private static int UsMap(char ch) => ch switch
    {
        >= 'a' and <= 'z' => char.ToUpperInvariant(ch),
        >= 'A' and <= 'Z' => ch | 0x100,
        >= '0' and <= '9' => ch,
        ' ' => 32,
        '!' => 49 | 0x100,
        _ => -1
    };

    public static void Run()
    {
        count = 0;
        FactorySoftware.CheckNames([]);
        FactorySoftware.CheckNames(["notepad", "vaydeer", "Vaydeer notes"]);
        Check(true, "unrelated processes do not block CLI");
        foreach (string name in new[] { "Vaydeer keyboard", "vaydeer KEYBOARD", "Vaydeer.Keyboard.Win", "Vaydeer.Keyboard.Win1" })
            Reject(() => FactorySoftware.CheckNames([name]), "factory software conflict detected before USB access");
        KeyConfig[] input = Configuration.Parse("""[{"key":1,"name":"Build","text":"make","enter":true}]""");
        Check(Configuration.Compile(input[0] with { Name = new string('N', 16) }, UsMap).Header.Length == 36, "firmware 16-character key-label boundary");
        Reject(() => Configuration.Compile(input[0] with { Name = new string('N', 17) }, UsMap), "long key label rejected before USB write");
        Reject(() => Configuration.Parse("[{\"key\":1,\"name\":\"" + new string('N', 17) + "\",\"text\":\"x\"}]"), "JSON name limit matches firmware");
        var macro = Configuration.Compile(input[0], UsMap);
        Check(macro.Header.SequenceEqual(new byte[] {255,5,0,0}.Concat(Encoding.BigEndianUnicode.GetBytes("Build"))), "macro header");
        Check(Convert.ToHexString(macro.Data) == "4D0005004D01050041000500410105004B0005004B01050045000500450105000D0005000D013200", "vendor-compatible make plus Enter vector");
        var plain = Configuration.Compile(input[0] with { Enter = false }, UsMap);
        Check(plain.Data.Length == 32 && plain.Data[^4] == 69, "no implicit Enter");
        Check(!Configuration.Parse("""[{"key":1,"name":"A","text":"x"}]""")[0].Enter, "Enter defaults false");
        var shifted = Configuration.Compile(input[0] with { Text = "A!", Enter = false }, UsMap);
        Check(shifted.Data.Length == 32 && shifted.Data[0] == 16 && shifted.Data[12] == 16 && shifted.Data[13] == 1, "uppercase and punctuation release Shift");
        Check(Configuration.Compile(input[0] with { Text = "\r\n\t", Enter = false }, UsMap).Data.Length == 16, "CRLF emits one Enter and tab emits Tab");
        Check(Configuration.Compile(input[0] with { Text = "", Enter = true }, UsMap).Data.Length == 8, "Enter-only macro");
        var empty = Configuration.Compile(input[0] with { Text = "", Enter = false }, UsMap);
        Check(empty.Header[1] == 0 && empty.Data.SequenceEqual(new byte[] { 0 }), "empty text unassigns key");
        int Interval(KeyRecord key, int eventIndex) => key.Data[eventIndex * 4 + 2] | key.Data[eventIndex * 4 + 3] << 8;
        var timedConfig = input[0] with { Text = "ab", DelayMs = 20, EnterDelayMs = 100 };
        var timed = Configuration.Compile(timedConfig, UsMap);
        Check(Interval(timed, 0) == 5 && Interval(timed, 1) == 20 && Interval(timed, 3) == 100, "character gap and pre-Enter gap encoded separately");
        Check(Interval(timed, 4) == 5 && Interval(timed, 5) == 50, "Enter hold and final settling delay unchanged");
        var fallbackTiming = Configuration.Compile(timedConfig with { EnterDelayMs = null }, UsMap);
        Check(Interval(fallbackTiming, 3) == 20, "Enter gap falls back to character gap");
        var enterOnlyTiming = Configuration.Compile(input[0] with { Text = "", DelayMs = 500, EnterDelayMs = 600 }, UsMap);
        Check(enterOnlyTiming.Data.SequenceEqual(Configuration.Compile(input[0] with { Text = "" }, UsMap).Data), "no initial delay for Enter-only macro");
        var maximumTiming = Configuration.Compile(timedConfig with { DelayMs = 65535, EnterDelayMs = 65535 }, UsMap);
        Check(Interval(maximumTiming, 1) == 65535 && Interval(maximumTiming, 3) == 65535 && Interval(maximumTiming, 5) == 50, "16-bit maximum and clearing final high byte");
        var zeroTiming = Configuration.Compile(timedConfig with { DelayMs = 0, EnterDelayMs = 0 }, UsMap);
        Check(Interval(zeroTiming, 1) == 0 && Interval(zeroTiming, 3) == 0 && Interval(zeroTiming, 0) == 5, "zero gaps preserve key holds");
        var shiftedTiming = Configuration.Compile(timedConfig with { Text = "A!", Enter = false }, UsMap);
        Check(Interval(shiftedTiming, 2) == 5 && Interval(shiftedTiming, 3) == 20 && Interval(shiftedTiming, 7) == 50, "pause follows modifier release");
        var controlTiming = Configuration.Compile(timedConfig with { Text = "\r\n\t", Enter = false }, UsMap);
        Check(controlTiming.Data.Length == 16 && Interval(controlTiming, 1) == 20, "timing for CRLF and Tab");
        var ignoredEnterTiming = Configuration.Compile(timedConfig with { Enter = false, EnterDelayMs = 400 }, UsMap);
        Check(ignoredEnterTiming.Data.SequenceEqual(Configuration.Compile(timedConfig with { Enter = false, EnterDelayMs = null }, UsMap).Data), "Enter delay ignored without optional Enter");
        Check(Configuration.Compile(input[0] with { DelayMs = 5, EnterDelayMs = 5 }, UsMap).Data.SequenceEqual(macro.Data), "explicit defaults preserve legacy bytes");
        foreach (string field in new[] { "delayMs", "enterDelayMs" })
            foreach (string value in new[] { "-1", "65536", "1.5", "null", "true", "\"20\"" })
                Reject(() => Configuration.Parse("[{\"key\":1,\"name\":\"A\",\"text\":\"x\",\"" + field + "\":" + value + "}]"), "invalid timing value");
        Reject(() => Configuration.Compile(timedConfig with { DelayMs = -1 }, UsMap), "compiler rejects invalid timing before encoding");
        char? UsDecode(byte code, int modifiers)
        {
            if (code is >= 65 and <= 90 && modifiers is 0 or 1)
                return modifiers == 1 ? (char)code : char.ToLowerInvariant((char)code);
            if (code == 49 && modifiers == 1) return '!';
            if (modifiers == 0 && code is >= 48 and <= 57 or 32) return (char)code;
            return null;
        }
        CheckSteps(UsMap, UsDecode);
        KeyConfig Export(KeyRecord record) => (KeyConfig)Dump.Convert(record, UsDecode, UsMap);
        var exportedTiming = Export(timed);
        Check(exportedTiming.DelayMs == 20 && exportedTiming.EnterDelayMs == 100, "dump infers separate timing fields");
        Check(Configuration.Compile(exportedTiming, UsMap).Data.SequenceEqual(timed.Data), "timed dump reload preserves event bytes");
        Check(Export(fallbackTiming).DelayMs == 20 && Export(fallbackTiming).EnterDelayMs is null, "dump omits redundant Enter delay");
        Check(Configuration.Compile(Export(zeroTiming), UsMap).Data.SequenceEqual(zeroTiming.Data), "zero-gap dump reload");
        Check(Configuration.Compile(Export(maximumTiming), UsMap).Data.SequenceEqual(maximumTiming.Data), "maximum-gap dump reload");
        Check(Configuration.Compile(Export(shiftedTiming), UsMap).Data.SequenceEqual(shiftedTiming.Data), "shifted timed dump reload");
        Check(Configuration.Compile(Export(controlTiming), UsMap).Data.SequenceEqual(controlTiming.Data), "control-character timed dump reload");
        Check(Export(macro).DelayMs is null && Export(macro).EnterDelayMs is null, "dump omits legacy timing defaults");
        var unusualTiming = plain with { Data = (byte[])plain.Data.Clone() };
        unusualTiming.Data[2] = 90;
        Check(Dump.Convert(unusualTiming, UsDecode, UsMap) is RawDumpKey, "unrepresentable held-key timing retains raw data");
        using (var writer = new StringWriter())
        {
            var console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
            {
                Out = new Spectre.Console.AnsiConsoleOutput(writer),
                Ansi = Spectre.Console.AnsiSupport.No,
                Interactive = Spectre.Console.InteractionSupport.No
            });
            console.Profile.Width = 120;
            LoadOutput.Plan([timedConfig with { Name = "[red]literal[/]", Text = "make [debug]" }], "keys.json", console);
            string rendered = writer.ToString();
            Check(rendered.Contains("[red]literal[/]") && rendered.Contains("100 ms") && rendered.Contains("20 ms"), "Spectre load table renders timing and literal labels safely");
            Check(rendered.Contains("Macro") && rendered.Contains("make [debug]\\n"), "load table includes literal command preview and final Enter");
            LoadOutput.Plan([new KeyConfig { Key = 1, Name = "Find", Steps = [new() { Press = "Ctrl+F" }, new() { Text = "query" }] }], "steps.json", console);
            Check(writer.ToString().Contains("Ctrl+F") && writer.ToString().Contains("Steps"), "load table renders step macros without a top-level text field");
            LoadOutput.Completed(new(1, "backup[1].json"), console);
            rendered = writer.ToString();
            Check(rendered.Contains("stored settings verified") && rendered.Contains("backup[1].json") && rendered.Contains("system tray"), "Spectre success panel contains backup and factory-software reminder");
        }
        Check(LoadOutput.Preview(timedConfig with { Text = "a\r\nb\tc" }, 48) == "a\\nb\\tc\\n", "preview escapes emitted line breaks and tabs");
        Check(LoadOutput.Preview(timedConfig with { Text = "", Enter = false }, 48) == "(unassigned)", "preview identifies unassigned keys");
        Check(LoadOutput.Preview(timedConfig with { Text = new string('x', 80), Enter = false }, 20) == new string('x', 19) + "…", "long preview is bounded with ellipsis");
        Check(Export(macro).Text == "make" && Export(macro).Enter, "dump extracts trailing Enter");
        Check(Export(plain).Text == "make" && !Export(plain).Enter, "dump preserves no Enter");
        Check(Export(shifted).Text == "A!", "dump reconstructs Shift and punctuation");
        Check(Export(empty).Text == "" && !Export(empty).Enter, "dump empty macro");
        var multiline = Configuration.Compile(input[0] with { Text = "a\nb\t", Enter = true }, UsMap);
        Check(Export(multiline).Text == "a\nb\t" && Export(multiline).Enter, "dump preserves internal Enter and Tab");
        Check(Export(new(3, [255,0,255,0], [0])).Name == "Key 3", "dump names unassigned keys");
        var native = new KeyRecord(1, [255,2,0,0,0,65], Encoding.BigEndianUnicode.GetBytes("hi\r\n"));
        Check(Export(native).Text == "hi" && Export(native).Enter, "dump native Unicode text");
        Check(Dump.Convert(native with { Data = [0] }, UsDecode, UsMap) is RawDumpKey, "dump malformed Unicode as raw");
        Check(Dump.Convert(new(1, [255,1,255,0], [17,65]), UsDecode, UsMap) is KeyConfig { Steps.Length: 1 }, "dump Ctrl shortcut as loadable steps");
        Check(Dump.Convert(new(1, [255,5,1,0], plain.Data), UsDecode, UsMap) is RawDumpKey, "dump repeat macro as raw");
        Check(Dump.Convert(new(1, [255,5,0,0], [16,0,5,0]), UsDecode, UsMap) is RawDumpKey, "dump unbalanced modifier as raw");
        Check(Dump.Convert(new(1, [255,5,0,0], [65,0,5,0,66,1,5,0]), UsDecode, UsMap) is RawDumpKey, "dump overlapping events as raw");
        var raw = (RawDumpKey)Dump.Convert(new(1, [255,4,255,0], [1,2,3]), UsDecode, UsMap);
        Check(raw.HeaderHex == "FF04FF00" && raw.DataHex == "010203", "dump retains raw bytes");
        Reject(() => Configuration.Parse(System.Text.Json.JsonSerializer.Serialize(new[] {raw}, Configuration.Json)), "raw dump cannot silently load as blank text");
        Check(Configuration.Parse(System.Text.Json.JsonSerializer.Serialize(new[] {Export(macro)}, Configuration.Json))[0].Text == "make", "dump text is loadable JSON");
        using (var readOnlyDevice = new MemoryDevice { ReadOnly = true })
            Check(Dump.Read(new Protocol(readOnlyDevice), UsDecode, UsMap).Length == 9, "dump reads all keys without changing state");
        foreach (string bad in new[]
        {
            "[]", "null", "{}",
            """[{"key":0,"name":"A","text":"x"}]""",
            """[{"key":10,"name":"A","text":"x"}]""",
            """[{"key":1,"name":"A"}]""",
            """[{"key":1,"name":null,"text":"x"}]""",
            """[{"key":1,"name":"A","text":null}]""",
            """[{"key":1,"key":2,"name":"A","text":"x"}]""",
            """[{"key":1,"name":"A","text":"x","enetr":true}]""",
            """[{"key":1,"name":"A","text":"x"},{"key":1,"name":"B","text":"y"}]"""
        }) Reject(() => Configuration.Parse(bad), "malformed config");
        Reject(() => Configuration.Compile(input[0] with { Text = "🙂" }, UsMap), "unsupported surrogate");
        Reject(() => Configuration.Compile(input[0] with { Text = "é" }, UsMap), "unmappable character");
        Reject(() => Configuration.Compile(input[0] with { Text = "\b" }, UsMap), "unexpected control character");
        Reject(() => Configuration.Compile(input[0] with { Text = new string('x', 257) }, UsMap), "macro too long");
        byte[] valid = Reply(0x60, [1,2,3]);
        Check(Protocol.Parse(0x60, valid).SequenceEqual(new byte[] {1,2,3}), "padded reply with report ID");
        Check(Protocol.Parse(0x60, valid.AsSpan(1)).Length == 3, "reply without report ID");
        byte[] corrupt = (byte[])valid.Clone(); corrupt[4] ^= 1;
        Reject(() => Protocol.Parse(0x60, corrupt), "bad checksum");
        Reject(() => Protocol.Parse(0x62, valid), "wrong reply command");
        Reject(() => Protocol.Parse(0x60, valid.AsSpan(0, 5)), "truncated reply");
        Reject(() => Protocol.Parse(0x60, Reply(0x60, [], status: 1)), "device error");
        using (var device = new MemoryDevice())
        {
            var protocol = new Protocol(device);
            var longMacro = Configuration.Compile(input[0] with { Text = new string('x', 128), Enter = false }, UsMap);
            protocol.WriteLayer(new(0, "Layer1", [longMacro, .. Enumerable.Range(2, 8).Select(key => new KeyRecord(key, [255,0,255,0], [0]))]), 2);
            Check(Protocol.Equal(longMacro, protocol.ReadKey(0, 0)), "multipart data wraps sequence at 16");
            Reject(() => protocol.Command(0xFC), "firmware command denied");
        }
        string previous = Environment.CurrentDirectory;
        string directory = Path.Combine(Path.GetTempPath(), "vaydeer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.CurrentDirectory = directory;
            using var device = new MemoryDevice();
            var protocol = new Protocol(device);
            var before = protocol.Backup();
            var phases = new List<string>();
            var result = Program.Apply(protocol, [macro], phases.Add, quiet: true);
            Check(result.Count == 1 && File.Exists(result.BackupPath), "presentation receives verified result and real backup path");
            Check(phases.Count == 6 && phases[0].StartsWith("Reading") && phases[3].StartsWith("Writing") && phases[^1].StartsWith("Activating"), "load progress follows real operation stages");
            Check(device.ActiveLayer == 0, "load selects layer 1");
            Check(Protocol.Equal(macro, protocol.ReadKey(0, 0)), "macro written and verified");
            Check(Protocol.Equal(before.Layers[0].Keys[1], protocol.ReadKey(0, 1)), "unlisted key preserved");
            Check(Protocol.Equal(before.Layers[1].Keys[0], protocol.ReadKey(1, 0)), "other layer preserved");
            Check(Directory.GetFiles("backups").Length == 1, "backup created before change");
            var saved = System.Text.Json.JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Directory.GetFiles("backups")[0]), Configuration.Json)!;
            Check(Program.ValidateSnapshot(saved).Zip(before.Layers[0].Keys).All(pair => Protocol.Equal(pair.First, pair.Second)), "snapshot can restore exact raw records");
            Reject(() => Program.SaveBackup(before, Directory.GetFiles("backups")[0]), "existing backup not overwritten");
            device.FailNextCommit = true;
            var next = macro with { Data = plain.Data };
            Reject(() => Program.Apply(protocol, [next]), "load failure returns error");
            Check(Protocol.Equal(macro, protocol.ReadKey(0, 0)), "rollback restores previous assignment");
            device.FailNextCommit = true;
            string recoveryMessage = "";
            try { Program.Apply(protocol, [next], _ => throw new InvalidOperationException("Display failed"), quiet: true); }
            catch (IOException error) { recoveryMessage = error.Message; }
            Check(Protocol.Equal(macro, protocol.ReadKey(0, 0)), "renderer failure cannot interrupt rollback");
            Check(recoveryMessage.Contains("restored and verified") && recoveryMessage.Contains("Backup:"), "load error contains recovery outcome and backup path after live output stops");
            var unsupported = before with { Layers = [before.Layers[0] with { Keys = [new(1, [255,5,3,0], []), .. before.Layers[0].Keys[1..]] }] };
            Reject(() => Program.ValidateSnapshot(unsupported), "multi-phase restore refused");
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            Directory.Delete(directory, true);
        }
        Console.WriteLine($"PASS: {count} offline checks. No real USB device was opened.");
    }

    private static byte[] Reply(byte command, byte[] data, byte status = 0)
    {
        var frame = Protocol.Frame(command, [status, .. data]);
        Array.Resize(ref frame, 65);
        return frame;
    }

    private static void CheckSteps(Func<char, int> map, Func<byte, int, char?> decode)
    {
        var control = Configuration.Parse("""[{"key":1,"name":"Find","steps":[{"press":"Ctrl+F"}]}]""")[0];
        var encoded = Configuration.Compile(control, map);
        Check(Convert.ToHexString(encoded.Data) == "11000500460005004601050011013200", "Ctrl+F press/release order");
        var saveAs = control with { Steps = [new() { Press = "Ctrl+Shift+S" }] };
        var saveData = Configuration.Compile(saveAs, map).Data;
        Check(saveData.Length == 24 && saveData[0] == 17 && saveData[4] == 16 && saveData[8] == 83
            && saveData[16] == 16 && saveData[17] == 1 && saveData[20] == 17 && saveData[21] == 1,
            "chords release modifiers in reverse order");
        Check(Configuration.Compile(control with { Steps = [new() { Press = "control + shift + s" }] }, map).Data.SequenceEqual(saveData), "case-insensitive modifier aliases");
        foreach (int number in Enumerable.Range(1, 12))
            Check(Configuration.Compile(control with { Steps = [new() { Press = $"F{number}" }] }, map).Data[0] == 111 + number, "F1–F12 codes");
        foreach (string press in new[] { "Alt+F4", "Win+D", "Windows+Left", "Escape", "Esc", "Return", "Tab", "Space", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Left", "Up", "Right", "Down", "Ctrl+Shift+Equals" })
            Check(Configuration.Compile(control with { Steps = [new() { Press = press }] }, map).Data.Length >= 8, "supported named keys");
        var mixed = control with { DelayMs = 20, Steps = [new() { Press = "Ctrl+F" }, new() { Text = "find!" }, new() { Press = "Enter" }, new() { DelayMs = 100 }, new() { Press = "F3" }] };
        var mixedRecord = Configuration.Compile(mixed, map);
        Check(mixed.CharacterCount == 5 && mixedRecord.Data[^8] == 114 && mixedRecord.Data[^10] == 100, "mixed text, Enter, pause, and F3");
        var exported = (KeyConfig)Dump.Convert(mixedRecord, decode, map);
        Check(exported.Steps is not null && Configuration.Compile(exported, map).Data.SequenceEqual(mixedRecord.Data), "mixed steps dump preserves all event bytes and delays");
        Check(Configuration.Parse(System.Text.Json.JsonSerializer.Serialize(new[] { exported }, Configuration.Json))[0].Steps is not null, "steps dump JSON is accepted by loader");
        var trailing = control with { Steps = [new() { Press = "F1" }, new() { DelayMs = 65535 }] };
        var trailingRecord = Configuration.Compile(trailing, map);
        Check(trailingRecord.Data[^2] == 255 && trailingRecord.Data[^1] == 255, "explicit trailing pause retained");
        Check(Configuration.Compile((KeyConfig)Dump.Convert(trailingRecord, decode, map), map).Data.SequenceEqual(trailingRecord.Data), "trailing pause dump/reload");
        var zero = Configuration.Compile(control with { Steps = [new() { Press = "F1" }, new() { DelayMs = 0 }, new() { Press = "F2" }] }, map);
        Check(zero.Data[6] == 0 && zero.Data[7] == 0, "explicit zero pause replaces default gap");
        Check(Configuration.Compile(control with { Steps = [new() { Text = "" }] }, map).Header[1] == 0, "empty text step unassigns key");
        Check(Configuration.Compile(control with { Steps = Enumerable.Repeat(new MacroStep { Press = "F1" }, 256).ToArray() }, map).Data.Length == 2048, "512-event boundary across steps");
        Reject(() => Configuration.Compile(control with { Steps = Enumerable.Repeat(new MacroStep { Press = "F1" }, 257).ToArray() }, map), "step event limit");
        Check(LoadOutput.Preview(mixed, 100).Contains("Ctrl+F → \"find!\" → Enter → wait 100 ms → F3"), "steps appear in load preview");
        foreach (string press in new[] { "", " ", "Ctrl", "Ctrl+", "+F1", "Ctrl++A", "A+Ctrl", "Ctrl+Control+A", "Ctrl+Shift+Shift+A", "F0", "F13", "F24", "Fn+F1", "Ctrl+Unknown" })
            Reject(() => Configuration.Compile(control with { Steps = [new() { Press = press }] }, map), "invalid chord rejected");
        foreach (string json in new[]
        {
            """[{"key":1,"name":"A","text":"x","steps":[{"press":"F1"}]}]""",
            """[{"key":1,"name":"A","steps":null}]""",
            """[{"key":1,"name":"A","steps":[]}]""",
            """[{"key":1,"name":"A","steps":[null]}]""",
            """[{"key":1,"name":"A","steps":[{}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":null}]}]""",
            """[{"key":1,"name":"A","steps":[{"text":null}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1","text":"x"}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1","press":"F2"}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1","delayMs":5}]}]""",
            """[{"key":1,"name":"A","steps":[{"delayMs":1}]}]""",
            """[{"key":1,"name":"A","steps":[{"text":""},{"delayMs":1}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1"},{"delayMs":1},{"delayMs":2}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1"},{"delayMs":-1}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1"},{"delayMs":65536}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1"},{"delayMs":1.5}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1"},{"delayMs":null}]}]""",
            """[{"key":1,"name":"A","steps":[{"down":"Ctrl"}]}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1"}],"enter":false}]""",
            """[{"key":1,"name":"A","steps":[{"press":"F1"}],"enterDelayMs":5}]"""
        }) Reject(() => Configuration.Parse(json), "invalid steps JSON");
    }

    private sealed class MemoryDevice : IReports
    {
        private readonly KeyRecord[][] keys = Enumerable.Range(0, 2)
            .Select(_ => Enumerable.Range(1, 9).Select(key => new KeyRecord(key, [255,0,255,0], [0])).ToArray()).ToArray();
        private int readOffset, writeLayer, writeKey, writePart;
        private byte[] writeHeader = [];
        private readonly List<byte> writeData = [];
        private int stagedLayer = -1;
        private readonly HashSet<byte> stagedKeys = [];
        public int ActiveLayer = 1;
        public bool FailNextCommit;
        public bool ReadOnly;

        public byte[] Exchange(byte[] report)
        {
            byte command = report[1];
            if (ReadOnly && command is not (0x60 or 0x62)) throw new InvalidOperationException("Dump attempted a state-changing command.");
            byte[] payload = report.AsSpan(3, report[2]).ToArray();
            byte[] result;
            switch (command)
            {
                case 0x60: result = [1,9,1,1,5,0,2,2,(byte)ActiveLayer,2,6]; break;
                case 0x67: result = Encoding.BigEndianUnicode.GetBytes($"Layer{payload[0] + 1}"); break;
                case 0x62:
                    var key = keys[payload[1]][payload[2]];
                    if (payload[0] == 255) { readOffset = 0; result = key.Header; }
                    else if (readOffset >= key.Data.Length) result = [254];
                    else
                    {
                        if (payload[0] != (readOffset / 48) % 16) throw new InvalidDataException("Bad read sequence.");
                        result = [payload[0], .. key.Data.AsSpan(readOffset, Math.Min(48, key.Data.Length - readOffset))];
                        readOffset += 48;
                    }
                    break;
                case 0x61:
                    if (payload[0] == 255)
                    {
                        if (stagedLayer != payload[1]) throw new InvalidOperationException("Layer header must precede key writes.");
                        writeLayer = payload[1]; writeKey = payload[2]; writePart = 0;
                        stagedKeys.Add(payload[2]);
                        writeHeader = [255, .. payload.AsSpan(3)]; writeData.Clear();
                    }
                    else if (payload[0] == 254) keys[writeLayer][writeKey] = new(writeKey + 1, writeHeader, writeData.ToArray());
                    else
                    {
                        if (payload[0] != writePart++ % 16) throw new InvalidDataException("Bad write sequence.");
                        writeData.AddRange(payload.AsSpan(1).ToArray());
                    }
                    result = []; break;
                case 0x66:
                    if (stagedKeys.Count != 9) throw new InvalidOperationException("All nine keys must be staged before commit.");
                    if (FailNextCommit) { FailNextCommit = false; throw new IOException("Simulated commit failure."); }
                    stagedLayer = -1;
                    result = []; break;
                case 0x65: stagedLayer = payload[0]; stagedKeys.Clear(); result = []; break;
                case 0x64: ActiveLayer = payload[0]; result = []; break;
                default: throw new InvalidOperationException("Unexpected test command.");
            }
            return Reply(command, result);
        }
        public void Dispose() { }
    }
}
