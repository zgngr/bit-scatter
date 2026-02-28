using BitScatter.Application.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BitScatter.Cli.Commands;

public class DownloadCommandSettings : CommandSettings
{
    [CommandArgument(0, "<file-id>")]
    public string FileId { get; set; } = string.Empty;

    [CommandArgument(1, "<output-path>")]
    public string OutputPath { get; set; } = string.Empty;
}

public class DownloadCommand : AsyncCommand<DownloadCommandSettings>
{
    private readonly IDownloadService _downloadService;

    public DownloadCommand(IDownloadService downloadService)
    {
        _downloadService = downloadService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, DownloadCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(settings.FileId, out var fileId))
        {
            AnsiConsole.MarkupLine("[red]Invalid file ID format. Must be a valid GUID.[/]");
            return 1;
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
