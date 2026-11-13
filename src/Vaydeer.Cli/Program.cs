using System.Text.Json;

namespace Vaydeer;

internal static class Program
{
    private const string Help = """
        Vaydeer — load text macros onto layer 1 of a nine-key keypad

        vaydeer load keys.json       Back up, program listed keys, verify, select layer 1
        vaydeer validate keys.json   Check JSON and compile macros without accessing USB
        vaydeer info                 Show connected keypad information
        vaydeer dump [file.json]     Read layer 1 as JSON (stdout or a new file)
        vaydeer backup [file.json]   Save a snapshot of all hardware layers
        vaydeer restore file.json    Restore layer 1 from a CLI snapshot
        vaydeer self-test            Run offline tests

        JSON: [{"key":1,"name":"Build","text":"make","enter":true}]
        Keys: 1–9, left to right, top to bottom. enter defaults to false.
        Only listed keys on layer 1 change; other keys and layers are preserved.
        Text uses the current Windows keyboard layout. Close other keypad editors.
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help") { Console.WriteLine(Help); return 0; }
        try
        {
            switch (args[0])
            {
                case "load" or "validate" when args.Length == 2:
                    var config = Configuration.Read(args[1]);
                    var map = Configuration.WindowsLayoutMapper();
                    KeyRecord[] compiled = config.Select(key => Configuration.Compile(key, map)).ToArray();
                    foreach (var key in config) Console.WriteLine($"Key {key.Key}: {key.Name} ({key.Text.Length} characters, Enter: {key.Enter})");
                    if (args[0] == "validate") { Console.WriteLine("Valid. No USB access or configuration changes."); return 0; }
                    FactorySoftware.EnsureStopped();
                    using (var device = WindowsHid.Connect()) Apply(new Protocol(device), compiled);
                    return 0;
                case "info" when args.Length == 1:
                    using (var device = WindowsHid.Connect())
                    {
                        var info = new Protocol(device).Info();
                        Console.WriteLine($"9-key Smart Keypad | Firmware {info.Firmware} | Active layer {info.ActiveLayer + 1} | {info.LayerCount} stored layers");
                    }
                    return 0;
                case "dump" when args.Length is 1 or 2:
                    object[] dump;
                    using (var device = WindowsHid.Connect())
                        dump = Dump.Read(new Protocol(device), Configuration.WindowsLayoutDecoder(), Configuration.WindowsLayoutMapper());
                    string json = JsonSerializer.Serialize(dump, Configuration.Json);
                    if (args.Length == 2)
                    {
                        using var output = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write, FileShare.None), new System.Text.UTF8Encoding(false));
                        output.WriteLine(json);
                        Console.Error.WriteLine($"Dump saved: {Path.GetFullPath(args[1])}");
                    }
                    else Console.WriteLine(json);
                    int rawCount = dump.Count(key => key is RawDumpKey);
                    if (rawCount != 0) Console.Error.WriteLine($"Note: {rawCount} key(s) have factory actions that cannot be converted to loadable text; raw records are included.");
                    return 0;
                case "backup" when args.Length is 1 or 2:
                    using (var device = WindowsHid.Connect())
                        Console.WriteLine($"Backup: {SaveBackup(new Protocol(device).Backup(), args.Length == 2 ? args[1] : null)}");
                    return 0;
                case "restore" when args.Length == 2:
                    var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(args[1]), Configuration.Json)
                        ?? throw new InvalidDataException("Empty snapshot.");
                    var keys = ValidateSnapshot(snapshot);
                    FactorySoftware.EnsureStopped();
                    using (var device = WindowsHid.Connect()) Apply(new Protocol(device), keys);
                    return 0;
                case "self-test" when args.Length == 1:
                    SelfTests.Run();
                    return 0;
                default:
                    Console.Error.WriteLine(Help);
                    return 2;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
                                     or ArgumentException or InvalidOperationException or TimeoutException
                                     or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"Error: {error.Message}");
            return 1;
        }
    }

    internal static string SaveBackup(Snapshot snapshot, string? path = null)
    {
        if (path is null)
        {
            Directory.CreateDirectory("backups");
            path = Path.Combine("backups", $"cli-{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.json");
        }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, snapshot, Configuration.Json);
        output.Flush(true);
        return Path.GetFullPath(path);
    }

    internal static KeyRecord[] ValidateSnapshot(Snapshot snapshot)
    {
        if (snapshot.Version != 1 || snapshot.DeviceInfo is not { Length: 11 } || snapshot.DeviceInfo[0] != 1
            || snapshot.DeviceInfo[1] != 9 || snapshot.Layers is null || snapshot.Layers.Any(layer => layer is null))
            throw new InvalidDataException("Invalid CLI snapshot format.");
        // Sequence macros have separate phases not represented by this reader.
        var layers = snapshot.Layers.Where(layer => layer.Index == 0).ToArray();
        if (layers.Length != 1 || layers[0].Keys is not { Length: 9 }) throw new InvalidDataException("Snapshot must contain nine layer-1 key records.");
        var numbers = new HashSet<int>();
        foreach (var key in layers[0].Keys)
        {
            if (key is null || key.Key is < 1 or > 9 || !numbers.Add(key.Key) || key.Header is not { Length: >= 4 and <= 52 }
                || key.Header[0] != 255 || (key.Header.Length - 4) % 2 != 0 || key.Data is null || key.Data.Length > 15360)
                throw new InvalidDataException("Snapshot contains an invalid key record.");
            if (key.Header[1] > 6 || (key.Header[1] == 5 && key.Header[2] == 3))
                throw new InvalidDataException("This snapshot contains an unsupported action type or multi-phase macro.");
        }
        return layers[0].Keys;
    }

    internal static void Apply(Protocol keypad, KeyRecord[] requested)
    {
        var before = keypad.Backup();
        string backup = SaveBackup(before);
        Console.WriteLine($"Backup: {backup}");
        var info = new DeviceInfo(before.DeviceInfo);
        var original = before.Layers[0].Keys;
        var changed = requested.Where(key => !Protocol.Equal(key, original.Single(old => old.Key == key.Key))).ToArray();
        foreach (var old in original)
        {
            if (old.Header[1] > 6) throw new InvalidOperationException($"Key {old.Key} uses an unsupported factory action type.");
            if (old.Header[1] == 5 && old.Header[2] == 3)
                throw new InvalidOperationException($"Key {old.Key} has a multi-phase macro; use the factory software to replace it first.");
        }
        // Abort before writing if another editor modified any layer-1 key during backup.
        keypad.VerifyLayer(0, original);
        bool attempted = false;
        bool activating = false;
        try
        {
            KeyRecord[] expected = original.Select(old => requested.SingleOrDefault(key => key.Key == old.Key) ?? old).ToArray();
            if (changed.Length != 0)
            {
                attempted = true;
                keypad.WriteLayer(before.Layers[0] with { Keys = expected }, info.LayerCount);
            }
            keypad.VerifyLayer(0, expected);
            for (int layer = 1; layer < before.Layers.Length; layer++) keypad.VerifyLayer(layer, before.Layers[layer].Keys);
            activating = true;
            keypad.Activate(0);
            if (keypad.Info().ActiveLayer != 0) throw new IOException("Layer 1 could not be activated.");
            Console.WriteLine($"Loaded {requested.Length} key(s); stored settings verified. Layer 1 is active. You can close this CLI.");
            Console.WriteLine("Keep the factory software exited while using these hardware macros.");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Load failed: {error.Message}");
            try
            {
                if (attempted) keypad.WriteLayer(before.Layers[0], info.LayerCount);
                if (activating) keypad.Activate(info.ActiveLayer);
                keypad.VerifyLayer(0, original);
                Console.Error.WriteLine("Previous layer-1 assignments restored and verified.");
            }
            catch (Exception rollback)
            {
                Console.Error.WriteLine($"Automatic recovery failed: {rollback.Message}");
                Console.Error.WriteLine($"Reconnect the keypad and use: vaydeer restore \"{backup}\"");
            }
            throw;
        }
    }
}
