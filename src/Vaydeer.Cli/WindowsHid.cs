using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Vaydeer;

internal interface IReports : IDisposable
{
    byte[] Exchange(byte[] report);
}

internal sealed class WindowsHid : IReports
{
    private readonly FileStream stream;
    private readonly int inputLength;
    private readonly int outputLength;

    private WindowsHid(string path, Caps caps)
    {
        var handle = Native.CreateFileW(path, 0xC0000000, 3, 0, 3, 0x40000000, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open keypad. Close the vendor editor and try again.");
        }
        try { stream = new FileStream(handle, FileAccess.ReadWrite, 1, true); }
        catch { handle.Dispose(); throw; }
        inputLength = caps.Input;
        outputLength = caps.Output;
    }

    public static WindowsHid Connect()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This CLI requires Windows.");
        Native.HidD_GetHidGuid(out var guid);
        nint set = Native.SetupDiGetClassDevsW(ref guid, null, 0, 0x12);
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        var candidates = new List<(string Path, Caps Caps)>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var entry = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, 0, ref guid, index, ref entry))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                Native.SetupDiGetDeviceInterfaceDetailW(set, ref entry, 0, 0, out uint size, 0);
                if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                nint buffer = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    Marshal.WriteInt32(buffer, nint.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref entry, buffer, size, out _, 0))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    string path = Marshal.PtrToStringUni(buffer + 4) ?? "";
                    if (!path.Contains("vid_0483&pid_5752", StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle = Native.CreateFileW(path, 0, 3, 0, 3, 0, 0);
                    if (handle.IsInvalid) continue;
                    if (!Native.HidD_GetPreparsedData(handle, out nint data)) continue;
                    Caps caps;
                    try
                    {
                        if (Native.HidP_GetCaps(data, out caps) != 0x110000)
                            throw new IOException("Cannot read HID capabilities.");
                    }
                    finally { Native.HidD_FreePreparsedData(data); }
                    if (caps.Page == 0xFF00 && caps.Usage == 1 && caps.Input == 65 && caps.Output == 65)
                        candidates.Add((path, caps));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        if (candidates.Count == 0) throw new IOException("No supported 9-key Vaydeer keypad found (0483:5752).");
        if (candidates.Count != 1) throw new IOException("Connect just one Vaydeer keypad.");
        return new WindowsHid(candidates[0].Path, candidates[0].Caps);
    }

    public byte[] Exchange(byte[] report) => ExchangeAsync(report).GetAwaiter().GetResult();

    private async Task<byte[]> ExchangeAsync(byte[] report)
    {
        if (report.Length > outputLength) throw new InvalidDataException("HID report is too large.");
        var output = new byte[outputLength];
        report.CopyTo(output, 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await stream.WriteAsync(output, timeout.Token);
            var input = new byte[inputLength];
            int count = await stream.ReadAsync(input, timeout.Token);
            if (count != inputLength) throw new IOException($"Incomplete HID report: {count} bytes.");
            return input;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Keypad timed out. Close the vendor editor, reconnect the keypad, and retry.");
        }
    }

    public void Dispose() => stream.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        public int Size;
        public Guid Guid;
        public uint Flags;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Caps
    {
        public ushort Usage, Page, Input, Output, Feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public ushort[] Counts;
    }

    private static class Native
    {
        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")][return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);
        [DllImport("hid.dll")][return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_FreePreparsedData(nint data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(nint data, out Caps caps);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint SetupDiGetClassDevsW(ref Guid guid, string? enumerator, nint parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData entry);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref InterfaceData entry, nint detail, uint size, out uint needed, nint device);
        [DllImport("setupapi.dll")][return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(nint set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    }
}
