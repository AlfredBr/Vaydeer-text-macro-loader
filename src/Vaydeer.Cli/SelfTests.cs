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
        char? UsDecode(byte code, int modifiers)
        {
            if (code is >= 65 and <= 90 && modifiers is 0 or 1)
                return modifiers == 1 ? (char)code : char.ToLowerInvariant((char)code);
            if (code == 49 && modifiers == 1) return '!';
            if (modifiers == 0 && code is >= 48 and <= 57 or 32) return (char)code;
            return null;
        }
        KeyConfig Export(KeyRecord record) => (KeyConfig)Dump.Convert(record, UsDecode, UsMap);
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
        Check(Dump.Convert(new(1, [255,1,255,0], [17,65]), UsDecode, UsMap) is RawDumpKey, "dump Ctrl shortcut as raw");
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
            Program.Apply(protocol, [macro]);
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
