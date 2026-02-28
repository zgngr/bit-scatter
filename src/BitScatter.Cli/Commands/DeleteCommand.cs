using BitScatter.Application.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BitScatter.Cli.Commands;

public class DeleteCommandSettings : CommandSettings
{
    [CommandArgument(0, "<file-id>")]
    public string FileId { get; set; } = string.Empty;
}

public class DeleteCommand : AsyncCommand<DeleteCommandSettings>
{
    private readonly IDeleteService _deleteService;

    public DeleteCommand(IDeleteService deleteService)
    {
        _deleteService = deleteService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, DeleteCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(settings.FileId, out var fileId))
        {
            AnsiConsole.MarkupLine("[red]Invalid file ID format. Must be a valid GUID.[/]");
            return 1;
        }

        var result = await _deleteService.DeleteAsync(fileId, cancellationToken);

        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]File {fileId} deleted successfully.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Delete failed: {result.ErrorMessage}[/]");
        return 1;
    }
}
