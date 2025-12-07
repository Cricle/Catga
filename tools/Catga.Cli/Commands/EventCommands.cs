using System.CommandLine;
using Spectre.Console;

namespace Catga.Cli.Commands;

/// <summary>
/// Event-related CLI commands.
/// </summary>
public static class EventCommands
{
    public static Command Create()
    {
        var command = new Command("events", "Event store operations");

        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateReadCommand());
        command.AddCommand(CreateHistoryCommand());

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all event streams");

        command.SetHandler(() =>
        {
            AnsiConsole.MarkupLine("[yellow]Event Streams[/]");
            AnsiConsole.WriteLine();

            var table = new Table();
            table.AddColumn("Stream ID");
            table.AddColumn("Version");
            table.AddColumn("Event Count");

            // Demo data - in real implementation, connect to event store
            table.AddRow("Order-order-1", "5", "5");
            table.AddRow("Order-order-2", "3", "3");
            table.AddRow("Customer-cust-1", "2", "2");

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("\n[grey]Connect to a real event store with --connection[/]");
        });

        return command;
    }

    private static Command CreateReadCommand()
    {
        var streamIdArg = new Argument<string>("stream-id", "The stream ID to read");
        var fromOption = new Option<long>("--from", () => 0, "Start version");
        var countOption = new Option<int>("--count", () => 100, "Max events to read");

        var command = new Command("read", "Read events from a stream")
        {
            streamIdArg,
            fromOption,
            countOption
        };

        command.SetHandler((streamId, from, count) =>
        {
            AnsiConsole.MarkupLine($"[yellow]Events in stream: {streamId}[/]");
            AnsiConsole.MarkupLine($"[grey]From version {from}, max {count} events[/]");
            AnsiConsole.WriteLine();

            var table = new Table();
            table.AddColumn("Version");
            table.AddColumn("Event Type");
            table.AddColumn("Timestamp");
            table.AddColumn("Data Preview");

            // Demo data
            table.AddRow("0", "OrderCreated", "2024-01-15 10:30:00", "{ orderId: \"order-1\" }");
            table.AddRow("1", "ItemAdded", "2024-01-15 10:30:05", "{ productId: \"prod-1\" }");
            table.AddRow("2", "OrderShipped", "2024-01-15 14:00:00", "{ trackingNo: \"TRK123\" }");

            AnsiConsole.Write(table);
        }, streamIdArg, fromOption, countOption);

        return command;
    }

    private static Command CreateHistoryCommand()
    {
        var streamIdArg = new Argument<string>("stream-id", "The stream ID");

        var command = new Command("history", "Show version history of a stream")
        {
            streamIdArg
        };

        command.SetHandler((streamId) =>
        {
            AnsiConsole.MarkupLine($"[yellow]Version History: {streamId}[/]");
            AnsiConsole.WriteLine();

            var chart = new BarChart()
                .Width(60)
                .Label("[green bold]Events per hour[/]")
                .CenterLabel()
                .AddItem("10:00", 3, Color.Green)
                .AddItem("11:00", 1, Color.Green)
                .AddItem("12:00", 0, Color.Grey)
                .AddItem("13:00", 0, Color.Grey)
                .AddItem("14:00", 2, Color.Green);

            AnsiConsole.Write(chart);
        }, streamIdArg);

        return command;
    }
}
