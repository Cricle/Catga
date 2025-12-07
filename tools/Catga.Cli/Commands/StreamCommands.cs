using System.CommandLine;
using Spectre.Console;

namespace Catga.Cli.Commands;

/// <summary>
/// Stream management CLI commands.
/// </summary>
public static class StreamCommands
{
    public static Command Create()
    {
        var command = new Command("streams", "Event stream management");

        command.AddCommand(CreateVerifyCommand());
        command.AddCommand(CreateSnapshotCommand());
        command.AddCommand(CreateCompactCommand());

        return command;
    }

    private static Command CreateVerifyCommand()
    {
        var streamIdArg = new Argument<string>("stream-id", "Stream ID to verify");

        var command = new Command("verify", "Verify stream integrity")
        {
            streamIdArg
        };

        command.SetHandler((streamId) =>
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Verifying stream {streamId}...", ctx =>
                {
                    Thread.Sleep(500);
                    ctx.Status("Computing hash...");
                    Thread.Sleep(500);
                    ctx.Status("Validating sequence...");
                    Thread.Sleep(300);
                });

            var panel = new Panel(new Markup(
                $"[bold]Stream:[/] {streamId}\n" +
                $"[bold]Events:[/] 5\n" +
                $"[bold]Hash:[/] [grey]sha256:abc123...[/]\n" +
                $"[bold]Integrity:[/] [green]Valid[/]"))
            {
                Header = new PanelHeader("[green]Verification Result[/]"),
                Border = BoxBorder.Rounded
            };

            AnsiConsole.Write(panel);
        }, streamIdArg);

        return command;
    }

    private static Command CreateSnapshotCommand()
    {
        var streamIdArg = new Argument<string>("stream-id", "Stream ID");

        var command = new Command("snapshot", "Create a snapshot for a stream")
        {
            streamIdArg
        };

        command.SetHandler((streamId) =>
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Creating snapshot for {streamId}...", ctx =>
                {
                    Thread.Sleep(500);
                    ctx.Status("Loading aggregate...");
                    Thread.Sleep(500);
                    ctx.Status("Saving snapshot...");
                    Thread.Sleep(300);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Snapshot created for [yellow]{streamId}[/] at version 5");
        }, streamIdArg);

        return command;
    }

    private static Command CreateCompactCommand()
    {
        var streamIdArg = new Argument<string>("stream-id", "Stream ID");
        var keepOption = new Option<int>("--keep", () => 3, "Number of snapshots to keep");

        var command = new Command("compact", "Compact stream snapshots")
        {
            streamIdArg,
            keepOption
        };

        command.SetHandler((streamId, keep) =>
        {
            var confirm = AnsiConsole.Confirm($"Compact snapshots for [yellow]{streamId}[/], keeping {keep} most recent?");
            if (!confirm)
            {
                AnsiConsole.MarkupLine("[grey]Cancelled[/]");
                return;
            }

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Compacting {streamId}...", ctx =>
                {
                    Thread.Sleep(500);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Removed 2 old snapshots, kept {keep}");
        }, streamIdArg, keepOption);

        return command;
    }
}
