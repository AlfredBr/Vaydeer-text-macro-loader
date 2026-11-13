using Spectre.Console;

namespace Vaydeer;

internal static class LoadOutput
{
    public static void Plan(KeyConfig[] keys, string source, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        console.Write(new Rule("[bold cyan]Vaydeer · Load macros[/]").LeftJustified());
        console.Write(new Text($"Source: {Path.GetFullPath(source)}\nLayer 1 · {keys.Length} key(s)\n\n"));
        var table = new Table().Border(TableBorder.Rounded);
        int previewWidth = Math.Clamp(console.Profile.Width - 76, 12, 48);
        foreach (string column in new[] { "Key", "Name", "Macro", "Characters", "Enter", "Gap", "Before Enter" })
            table.AddColumn(column);
        foreach (var key in keys)
            table.AddRow(new Text(key.Key.ToString()), new Text(key.Name), new Text(Preview(key, previewWidth)), new Text(key.CharacterCount.ToString()),
                new Markup(key.Steps is not null ? "[grey]Steps[/]" : key.Enter ? "[green]Yes[/]" : "[grey]No[/]"),
                new Text($"{key.DelayMs ?? 5} ms"),
                new Text(key.Steps is null && key.Enter && key.CharacterCount != 0 ? $"{key.EnterDelayMs ?? key.DelayMs ?? 5} ms" : "—"));
        console.Write(table);
        console.WriteLine();
    }

    internal static string Preview(KeyConfig key, int maxLength)
    {
        static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
        string preview = key.Steps is null ? Escape(key.Text) : string.Join(" → ", key.Steps.Select(step =>
            step.Press ?? (step.Text is string text ? "\"" + Escape(text).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : $"wait {step.DelayMs} ms")));
        if (key.Steps is null && key.Enter) preview += "\\n";
        if (preview.Length == 0) return "(unassigned)";
        return preview.Length <= maxLength ? preview : preview[..(maxLength - 1)] + "…";
    }

    public static void Run(Protocol keypad, KeyRecord[] keys)
    {
        LoadResult result;
        if (System.Console.IsOutputRedirected)
            result = Program.Apply(keypad, keys,
                phase => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(phase)}[/]"), quiet: true);
        else
            result = AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("cyan"))
                .Start("Reading keypad…", context =>
                    Program.Apply(keypad, keys, phase => context.Status(Markup.Escape(phase)), quiet: true));
        Completed(result);
    }

    public static void Completed(LoadResult result, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        console.Write(new Panel(new Markup($"[bold green]Loaded {result.Count} key(s) · stored settings verified[/]\nLayer 1 is active."))
            .Border(BoxBorder.Rounded).BorderColor(Color.Green));
        console.Write(new Text($"Backup: {result.BackupPath}\n"));
        console.MarkupLine("[yellow]Keep the factory software exited from the system tray while using these macros.[/]");
    }

    public static void Error(string message)
    {
        // Errors stay on stderr even when stdout is redirected.
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(System.Console.Error) });
        console.Write(new Panel(new Text(message)).Header("[red]Load failed[/]").BorderColor(Color.Red));
    }
}
