using BitScatter.Application.Interfaces;
using BitScatter.Application.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BitScatter.Cli.Commands;

public class DownloadCommandSettings : CommandSettings
{
    [CommandArgument(0, "<file-id>")]
    public string FileId { get; set; } = string.Empty;

    [CommandArgument(1, "<output-path>")]
    public string OutputPath { get; set; } = string.Empty;

    [CommandOption("-e|--password")]
    public string? DecryptionPassword { get; set; }
}

public class DownloadCommand : AsyncCommand<DownloadCommandSettings>
{
    private readonly IDownloadService _downloadService;
    private readonly IFileManifestRepository _repository;

    public DownloadCommand(IDownloadService downloadService, IFileManifestRepository repository)
    {
        _downloadService = downloadService;
        _repository = repository;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, DownloadCommandSettings settings, CancellationToken cancellationToken)
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

        var manifest = await _repository.GetByIdAsync(fileId, cancellationToken);
        if (manifest is null)
        {
            AnsiConsole.MarkupLine($"[red]Error: File manifest with ID '{fileId}' not found.[/]");
            return 1;
        }

        var password = settings.DecryptionPassword;
        if (manifest.IsEncrypted && string.IsNullOrEmpty(password))
        {
            password = AnsiConsole.Prompt(
                new TextPrompt<string>("[yellow]Enter decryption password:[/] ")
                    .Secret());
        }

        try
        {
            var result = await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Downloading...[/]");

                    var r = await _downloadService.DownloadAsync(
                        fileId,
                        settings.OutputPath,
                        decryptionPassword: password,
                        progress: new Progress<(int completed, int total)>(p =>
                        {
                            task.MaxValue = p.total;
                            task.Value = p.completed;
                        }),
                        cancellationToken: cancellationToken);

                    task.Value = task.MaxValue;
                    task.StopTask();
                    return r;
                });

            if (result.Success)
            {
                AnsiConsole.MarkupLine("[green]Download successful![/]");
                AnsiConsole.Write(new Table()
                    .AddColumn("Property")
                    .AddColumn("Value")
                    .AddRow("File ID", result.FileManifestId.ToString())
                    .AddRow("Output Path", result.OutputPath));
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]Download failed: {result.ErrorMessage}[/]");
            return 1;
        }
        catch (KeyNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Unexpected error: {ex.Message}[/]");
            return 1;
        }
    }
}
