using System.Diagnostics;

namespace Vaydeer;

internal static class FactorySoftware
{
    private static readonly string[] ProcessNames =
        ["Vaydeer keyboard", "Vaydeer.Keyboard.Win", "Vaydeer.Keyboard.Win1"];

    public static void EnsureStopped() => CheckNames(RunningNames());

    public static void CheckNames(IEnumerable<string> names)
    {
        if (!names.Any(name => ProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase))) return;
        throw new InvalidOperationException(
            "The Vaydeer factory software is still running. Fully exit it from its system-tray icon " +
            "(closing its window is not enough), then retry. It can paste old Software-mode text " +
            "even when the keypad contains new macros. No keypad settings were changed by this command.");
    }

    private static IEnumerable<string> RunningNames()
    {
        var running = new List<string>();
        foreach (string name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length != 0) running.Add(name);
            foreach (var process in processes) process.Dispose();
        }
        return running;
    }
}
