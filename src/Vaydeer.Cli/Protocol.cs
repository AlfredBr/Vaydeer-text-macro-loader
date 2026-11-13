using System.Text;

namespace Vaydeer;

internal record DeviceInfo(byte[] Raw)
{
    public int LayerCount => Raw[9];
    public int ActiveLayer => Raw[8];
    public string Firmware => string.Join('.', Raw[2..5]);
}

internal record KeyRecord(int Key, byte[] Header, byte[] Data);
internal record LayerRecord(int Index, string Name, KeyRecord[] Keys);
internal record Snapshot(int Version, DateTimeOffset CapturedAt, byte[] DeviceInfo, LayerRecord[] Layers);

internal sealed class Protocol(IReports reports)
{
    public static byte[] Frame(byte command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 61) throw new InvalidDataException("Protocol payload exceeds 61 bytes.");
        var result = new byte[payload.Length + 4];
        result[1] = command;
        result[2] = (byte)payload.Length;
        payload.CopyTo(result.AsSpan(3));
        for (int i = 1; i < result.Length - 1; i++) result[^1] ^= result[i];
        return result;
    }

    public static byte[] Parse(byte command, ReadOnlySpan<byte> report)
    {
        if (!report.IsEmpty && report[0] == 0) report = report[1..];
        if (report.Length < 3 || report[0] != command) throw new InvalidDataException("Unexpected keypad reply.");
        int end = report[1] + 2;
        if (report.Length <= end) throw new InvalidDataException("Truncated keypad reply.");
        byte checksum = 0;
        foreach (byte value in report[..(end + 1)]) checksum ^= value;
        if (checksum != 0) throw new InvalidDataException("Keypad reply checksum failed.");
        if (end < 3 || report[2] != 0) throw new IOException($"Keypad rejected command 0x{command:X2} (status {(end < 3 ? -1 : report[2])}).");
        return report[3..end].ToArray();
    }

    public byte[] Command(byte command, params byte[] payload)
    {
        if (command is not (0x60 or 0x61 or 0x62 or 0x64 or 0x65 or 0x66 or 0x67))
            throw new InvalidOperationException("Unsupported command; firmware access is disabled.");
        return Parse(command, reports.Exchange(Frame(command, payload)));
    }

    public DeviceInfo Info()
    {
        byte[] raw = Command(0x60);
        if (raw.Length != 11 || raw[0] != 1 || raw[1] != 9 || raw[9] < 1 ||
            raw[9] > raw[10] || raw[10] > 6 || raw[8] >= raw[9])
            throw new InvalidDataException("Unsupported keypad or invalid layer information.");
        return new(raw);
    }

    public KeyRecord ReadKey(int layer, int key)
    {
        byte[] header = Command(0x62, 255, (byte)layer, (byte)key);
        if (header.Length < 4 || header[0] != 255 || (header.Length - 4) % 2 != 0)
            throw new InvalidDataException("Invalid key header.");
        var data = new List<byte>();
        for (int part = 0; part < 256; part++)
        {
            byte sequence = (byte)(part % 16);
            byte[] chunk = Command(0x62, sequence, (byte)layer, (byte)key);
            if (chunk.Length == 0) throw new InvalidDataException("Empty key data reply.");
            if (chunk[0] == 254) return new(key + 1, header, data.ToArray());
            if (chunk[0] != sequence) throw new InvalidDataException("Key chunk sequence mismatch.");
            data.AddRange(chunk.AsSpan(1).ToArray());
        }
        throw new InvalidDataException("Key data exceeded the bounded read limit.");
    }

    public Snapshot Backup()
    {
        DeviceInfo info = Info();
        var layers = new List<LayerRecord>();
        for (int layer = 0; layer < info.LayerCount; layer++)
        {
            string name = Encoding.BigEndianUnicode.GetString(Command(0x67, (byte)layer));
            var keys = Enumerable.Range(0, 9).Select(key => ReadKey(layer, key)).ToArray();
            layers.Add(new(layer, name, keys));
        }
        return new(1, DateTimeOffset.UtcNow, info.Raw, layers.ToArray());
    }

    private void WriteKey(int layer, KeyRecord key)
    {
        if (layer is < 0 or > 5 || key.Key is < 1 or > 9 || key.Header.Length is < 4 or > 52 || key.Header[0] != 255)
            throw new InvalidDataException("Invalid key record.");
        // Inserting the layer and key index turns a read header into a write header.
        Command(0x61, [255, (byte)layer, (byte)(key.Key - 1), .. key.Header.AsSpan(1)]);
        for (int offset = 0, part = 0; offset < key.Data.Length; offset += 60, part++)
            Command(0x61, [(byte)(part % 16), .. key.Data.AsSpan(offset, Math.Min(60, key.Data.Length - offset))]);
        Command(0x61, 254);
    }

    public void Commit(int layer, int layerCount) => Command(0x66, (byte)layer, (byte)(layerCount - 1));
    public void WriteLayer(LayerRecord layer, int layerCount)
    {
        if (layer.Keys.Length != 9 || layer.Name.Length > 24)
            throw new InvalidDataException("Cannot stage this layer.");
        Command(0x65, [(byte)layer.Index, (byte)(layerCount - 1), .. Encoding.BigEndianUnicode.GetBytes(layer.Name)]);
        foreach (var key in layer.Keys.OrderBy(key => key.Key)) WriteKey(layer.Index, key);
        Commit(layer.Index, layerCount);
    }
    public void Activate(int layer) => Command(0x64, (byte)layer);
    public static bool Equal(KeyRecord a, KeyRecord b) => a.Key == b.Key && a.Header.SequenceEqual(b.Header) && a.Data.SequenceEqual(b.Data);

    public void VerifyLayer(int layer, KeyRecord[] expected)
    {
        foreach (var key in expected)
            if (!Equal(key, ReadKey(layer, key.Key - 1)))
                throw new IOException($"Read-back mismatch on layer {layer + 1}, key {key.Key}.");
    }
}
