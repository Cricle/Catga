using System.CommandLine;
using Catga.Cli.Commands;

var rootCommand = new RootCommand("Catga CLI - Event Sourcing Management Tool")
{
    EventCommands.Create(),
    ProjectionCommands.Create(),
    FlowCommands.Create(),
    StreamCommands.Create()
};

return await rootCommand.InvokeAsync(args);
