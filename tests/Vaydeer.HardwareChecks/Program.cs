using Vaydeer;

// Explicit opt-in: a round trip temporarily installs a harmless text-only macro.
if (args is not ["--write-unchanged"] && args is not ["--round-trip"])
{
    Console.Error.WriteLine("Use --write-unchanged or --round-trip (temporarily loads text, then restores the original layer).");
    return 2;
}
using var device = WindowsHid.Connect();
var keypad = new Protocol(device);
var before = keypad.Backup();
Console.WriteLine("Backup: " + Vaydeer.Program.SaveBackup(before));
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
