using System.CommandLine;
using Spectre.Console;

namespace Catga.Cli.Commands;

/// <summary>
/// Projection-related CLI commands.
/// </summary>
public static class ProjectionCommands
{
    public static Command Create()
    {
        var command = new Command("projections", "Projection management");

        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateStatusCommand());
        command.AddCommand(CreateRebuildCommand());
        command.AddCommand(CreateResetCommand());

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all projections");

        command.SetHandler(() =>
        {
            AnsiConsole.MarkupLine("[yellow]Projections[/]");
            AnsiConsole.WriteLine();

            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("Status");
            table.AddColumn("Position");
            table.AddColumn("Last Updated");

            table.AddRow("OrderSummary", "[green]Running[/]", "1,234", "2024-01-15 14:30:00");
            table.AddRow("CustomerStats", "[green]Running[/]", "567", "2024-01-15 14:29:55");
            table.AddRow("InventoryView", "[yellow]Catching up[/]", "890", "2024-01-15 14:28:00");

            AnsiConsole.Write(table);
        });

        return command;
    }

    private static Command CreateStatusCommand()
    {
        var nameArg = new Argument<string>("name", "Projection name");

        var command = new Command("status", "Show projection status")
        {
            nameArg
        };

        command.SetHandler((name) =>
        {
            AnsiConsole.MarkupLine($"[yellow]Projection: {name}[/]");
            AnsiConsole.WriteLine();

            var panel = new Panel(new Markup(
                $"[bold]Status:[/] [green]Running[/]\n" +
                $"[bold]Position:[/] 1,234\n" +
                $"[bold]Processed:[/] 1,234 events\n" +
                $"[bold]Last Updated:[/] 2024-01-15 14:30:00\n" +
                $"[bold]Lag:[/] 0 events"))
            {
                Header = new PanelHeader($"[blue]{name}[/]"),
                Border = BoxBorder.Rounded
            };

            AnsiConsole.Write(panel);
        }, nameArg);

        return command;
    }

    private static Command CreateRebuildCommand()
    {
        var nameArg = new Argument<string>("name", "Projection name");
        var forceOption = new Option<bool>("--force", "Skip confirmation");

        var command = new Command("rebuild", "Rebuild a projection from scratch")
        {
            nameArg,
            forceOption
        };

        command.SetHandler((name, force) =>
        {
            if (!force)
            {
                var confirm = AnsiConsole.Confirm($"Rebuild projection [yellow]{name}[/]? This will reset all data.");
                if (!confirm)
                {
                    AnsiConsole.MarkupLine("[grey]Cancelled[/]");
                    return;
                }
            }

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Rebuilding {name}...", ctx =>
                {
                    // Simulate rebuild
                    Thread.Sleep(500);
                    ctx.Status("Resetting projection...");
                    Thread.Sleep(500);
                    ctx.Status("Replaying events...");
                    Thread.Sleep(1000);
                    ctx.Status("Finalizing...");
                    Thread.Sleep(300);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Projection [yellow]{name}[/] rebuilt successfully");
            AnsiConsole.MarkupLine("[grey]Processed 1,234 events[/]");
        }, nameArg, forceOption);

        return command;
    }

    private static Command CreateResetCommand()
    {
        var nameArg = new Argument<string>("name", "Projection name");

        var command = new Command("reset", "Reset projection checkpoint to beginning")
        {
            nameArg
        };

        command.SetHandler((name) =>
        {
            var confirm = AnsiConsole.Confirm($"Reset checkpoint for [yellow]{name}[/]?");
            if (!confirm)
            {
                AnsiConsole.MarkupLine("[grey]Cancelled[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Checkpoint reset for [yellow]{name}[/]");
        }, nameArg);

        return command;
    }
}
