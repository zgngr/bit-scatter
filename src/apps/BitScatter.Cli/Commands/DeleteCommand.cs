using BitScatter.Application.Interfaces;
using BitScatter.Application.Helpers;
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
    private readonly IFileManifestRepository _repository;

    public DeleteCommand(IDeleteService deleteService, IFileManifestRepository repository)
    {
        _deleteService = deleteService;
        _repository = repository;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, DeleteCommandSettings settings, CancellationToken cancellationToken)
    {
        Guid fileId;
        try
        {
            fileId = await IdResolver.ResolveIdAsync(_repository, settings.FileId, cancellationToken);
        }
        catch (Exception ex) when (ex is FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
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
