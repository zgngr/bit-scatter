using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Domain.Enums;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BitScatter.Cli.Commands;

public class UploadCommandSettings : CommandSettings
{
    [CommandArgument(0, "<file-path>")]
    public string FilePath { get; set; } = string.Empty;

    [CommandOption("-c|--chunk-size")]
    public int ChunkSizeKb { get; set; } = 1024;

    [CommandOption("-p|--providers")]
    public string Providers { get; set; } = "all";
}

public class UploadCommand : AsyncCommand<UploadCommandSettings>
{
    private readonly IUploadService _uploadService;

    public UploadCommand(IUploadService uploadService)
    {
        _uploadService = uploadService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, UploadCommandSettings settings, CancellationToken cancellationToken)
    {
        var providers = ParseProviders(settings.Providers);
        var options = new UploadOptions
        {
            ChunkSizeBytes = settings.ChunkSizeKb * 1024,
            StorageProviders = providers
        };

        UploadResult? result = null;

        try
        {
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Uploading...[/]");
                    task.IsIndeterminate = true;

                    result = await _uploadService.UploadAsync(settings.FilePath, options);

                    task.Value = 100;
                    task.StopTask();
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Upload failed: {ex.Message}[/]");
            return 1;
        }

        if (result?.Success == true)
        {
            AnsiConsole.MarkupLine("[green]Upload successful![/]");
            AnsiConsole.Write(new Table()
                .AddColumn("Property")
                .AddColumn("Value")
                .AddRow("File ID", result.FileManifestId.ToString())
                .AddRow("File Name", result.FileName)
                .AddRow("Original Size", $"{result.OriginalSize:N0} bytes")
                .AddRow("Chunks", result.ChunkCount.ToString()));
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Upload failed: {result?.ErrorMessage}[/]");
        return 1;
    }

    private static StorageProviderType[]? ParseProviders(string input)
    {
        if (input.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return null; // null signals UploadService to use all registered providers

        return input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.ToLowerInvariant() switch
            {
                "filesystem" or "fs" => StorageProviderType.FileSystem,
                "database" or "db" => StorageProviderType.Database,
                _ => throw new ArgumentException($"Unknown provider: {p}")
            })
            .ToArray();
    }
}
