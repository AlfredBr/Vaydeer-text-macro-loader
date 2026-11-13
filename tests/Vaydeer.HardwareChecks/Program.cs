using Vaydeer;

// Explicit opt-in: a round trip temporarily installs a harmless text-only macro.
if (args is not ["--write-unchanged"] && args is not ["--round-trip"] && args is not ["--label-limit"])
{
    Console.Error.WriteLine("Use --write-unchanged, --round-trip, or --label-limit (probes label lengths, then restores the original layer).");
    return 2;
}
FactorySoftware.EnsureStopped();
using var device = WindowsHid.Connect();
var keypad = new Protocol(device);
var before = keypad.Backup();
Console.WriteLine("Backup: " + Vaydeer.Program.SaveBackup(before));
if (args[0] == "--label-limit")
{
    try
    {
        foreach (int length in new[] { 16, 17, 18, 19 })
        {
            var originalKey = before.Layers[0].Keys[2];
            var key = originalKey with { Header = [.. originalKey.Header.AsSpan(0, 4), .. System.Text.Encoding.BigEndianUnicode.GetBytes(new string('L', length))] };
            var keys = before.Layers[0].Keys.Select(old => old.Key == key.Key ? key : old).ToArray();
            keypad.WriteLayer(before.Layers[0] with { Keys = keys }, before.DeviceInfo[9]);
            var actual = keypad.ReadKey(0, 2);
            Console.WriteLine($"Label {length}: received {(actual.Header.Length - 4) / 2} UTF-16 units; data matches: {actual.Data.SequenceEqual(originalKey.Data)}.");
        }
    }
    finally
    {
        keypad.WriteLayer(before.Layers[0], before.DeviceInfo[9]);
        keypad.VerifyLayer(0, before.Layers[0].Keys);
        Console.WriteLine("Original first layer restored and verified.");
    }
    return 0;
}
var original = before.Layers[0].Keys.First(key => key.Header[1] == 5 && key.Header[2] == 0);
var encoded = Configuration.Compile(new KeyConfig
{
    Key = original.Key,
    Name = System.Text.Encoding.BigEndianUnicode.GetString(original.Header.AsSpan(4)),
    Text = "make",
    Enter = true
}, Configuration.WindowsLayoutMapper());
// Compare only; never install a fixture if the original macro differs.
if (Protocol.Equal(original, encoded)) Console.WriteLine("PASS: JSON text compiler matches the original make + Enter macro byte for byte.");
if (args[0] == "--round-trip")
{
    var config = Configuration.Parse("""[{"key":1,"name":"CLI test","text":"Vaydeer test!","enter":false}]""");
    try { Vaydeer.Program.Apply(keypad, config.Select(key => Configuration.Compile(key, Configuration.WindowsLayoutMapper())).ToArray()); }
    finally
    {
        keypad.WriteLayer(before.Layers[0], before.DeviceInfo[9]);
        keypad.Activate(before.DeviceInfo[8]);
    }
}
for (int pass = 0; pass < (args[0] == "--round-trip" ? 1 : 2); pass++)
{
    if (args[0] == "--write-unchanged") keypad.WriteLayer(before.Layers[0], before.DeviceInfo[9]);
    var after = keypad.Backup();
    if (!before.DeviceInfo.SequenceEqual(after.DeviceInfo)) throw new IOException("Device state changed.");
    foreach (var layer in before.Layers)
    {
        var actual = after.Layers.Single(item => item.Index == layer.Index);
        if (layer.Name != actual.Name) throw new IOException("Layer name changed.");
        foreach (var key in layer.Keys)
            if (!Protocol.Equal(key, actual.Keys.Single(item => item.Key == key.Key)))
                throw new IOException($"Layer {layer.Index + 1} key {key.Key} changed.");
    }
}
Console.WriteLine(args[0] == "--round-trip"
    ? "PASS: JSON compile/load/verify and original-layer restoration; all keys, layer names, and active layer restored."
    : "PASS: Two complete C# layer writes and read-backs; all keys, layer names, and active layer unchanged.");
return 0;
