using System.CommandLine;
using Spectre.Console;

namespace Catga.Cli.Commands;

/// <summary>
/// Flow-related CLI commands.
/// </summary>
public static class FlowCommands
{
    public static Command Create()
    {
        var command = new Command("flows", "Flow orchestration management");

        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateStatusCommand());
        command.AddCommand(CreateResumeCommand());
        command.AddCommand(CreateCancelCommand());

        return command;
    }

    private static Command CreateListCommand()
    {
        var statusOption = new Option<string?>("--status", "Filter by status (running, completed, failed, suspended)");

        var command = new Command("list", "List all flows")
        {
            statusOption
        };

        command.SetHandler((status) =>
        {
            AnsiConsole.MarkupLine("[yellow]Flows[/]");
            if (status != null)
                AnsiConsole.MarkupLine($"[grey]Filtered by status: {status}[/]");
            AnsiConsole.WriteLine();

            var table = new Table();
            table.AddColumn("Flow ID");
            table.AddColumn("Type");
            table.AddColumn("Status");
            table.AddColumn("Current Step");
            table.AddColumn("Started");

            table.AddRow("flow-001", "CreateOrder", "[green]Completed[/]", "-", "2024-01-15 10:00:00");
            table.AddRow("flow-002", "CreateOrder", "[yellow]Running[/]", "ValidateInventory", "2024-01-15 14:30:00");
            table.AddRow("flow-003", "ProcessPayment", "[red]Failed[/]", "ChargeCard", "2024-01-15 14:25:00");
            table.AddRow("flow-004", "ShipOrder", "[blue]Suspended[/]", "WaitForPickup", "2024-01-15 13:00:00");

            AnsiConsole.Write(table);
        }, statusOption);

        return command;
    }

    private static Command CreateStatusCommand()
    {
        var flowIdArg = new Argument<string>("flow-id", "Flow ID");

        var command = new Command("status", "Show detailed flow status")
        {
            flowIdArg
        };

        command.SetHandler((flowId) =>
        {
            AnsiConsole.MarkupLine($"[yellow]Flow: {flowId}[/]");
            AnsiConsole.WriteLine();

            // Flow info panel
            var infoPanel = new Panel(new Markup(
                $"[bold]Type:[/] CreateOrder\n" +
                $"[bold]Status:[/] [yellow]Running[/]\n" +
                $"[bold]Started:[/] 2024-01-15 14:30:00\n" +
                $"[bold]Current Step:[/] ValidateInventory"))
            {
                Header = new PanelHeader("[blue]Flow Info[/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(infoPanel);
            AnsiConsole.WriteLine();

            // Steps tree
            var root = new Tree($"[yellow]Flow Steps[/]");
            var step1 = root.AddNode("[green]✓[/] CreateOrder");
            step1.AddNode("[grey]Duration: 50ms[/]");

            var step2 = root.AddNode("[green]✓[/] ValidateCustomer");
            step2.AddNode("[grey]Duration: 120ms[/]");

            var step3 = root.AddNode("[yellow]→[/] ValidateInventory");
            step3.AddNode("[grey]In progress...[/]");

            var step4 = root.AddNode("[grey]○[/] ProcessPayment");
            var step5 = root.AddNode("[grey]○[/] SendConfirmation");

            AnsiConsole.Write(root);
        }, flowIdArg);

        return command;
    }

    private static Command CreateResumeCommand()
    {
        var flowIdArg = new Argument<string>("flow-id", "Flow ID to resume");

        var command = new Command("resume", "Resume a suspended or failed flow")
        {
            flowIdArg
        };

        command.SetHandler((flowId) =>
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Resuming flow {flowId}...", ctx =>
                {
                    Thread.Sleep(500);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Flow [yellow]{flowId}[/] resumed");
        }, flowIdArg);

        return command;
    }

    private static Command CreateCancelCommand()
    {
        var flowIdArg = new Argument<string>("flow-id", "Flow ID to cancel");
        var forceOption = new Option<bool>("--force", "Skip confirmation");

        var command = new Command("cancel", "Cancel a running flow")
        {
            flowIdArg,
            forceOption
        };

        command.SetHandler((flowId, force) =>
        {
            if (!force)
            {
                var confirm = AnsiConsole.Confirm($"Cancel flow [yellow]{flowId}[/]?");
                if (!confirm)
                {
                    AnsiConsole.MarkupLine("[grey]Cancelled[/]");
                    return;
                }
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Flow [yellow]{flowId}[/] cancelled");
        }, flowIdArg, forceOption);

        return command;
    }
}
