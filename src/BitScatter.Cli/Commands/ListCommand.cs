using BitScatter.Application.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BitScatter.Cli.Commands;

public class ListCommandSettings : CommandSettings { }

public class ListCommand : AsyncCommand<ListCommandSettings>
{
    private readonly IFileManifestRepository _repository;

    public ListCommand(IFileManifestRepository repository)
    {
        _repository = repository;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ListCommandSettings settings, CancellationToken cancellationToken)
    {
        var manifests = await _repository.GetAllAsync();

        if (manifests.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No files uploaded yet.[/]");
            return 0;
        }

        var table = new Table()
            .AddColumn("ID")
            .AddColumn("File Name")
            .AddColumn("Size")
            .AddColumn("Chunks")
            .AddColumn("Created At");

        foreach (var m in manifests)
        {
            table.AddRow(
                m.Id.ToString(),
                m.FileName,
                $"{m.OriginalSize:N0} bytes",
                m.Chunks.Count.ToString(),
                m.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
