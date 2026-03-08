using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Domain.Enums;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BitScatter.Cli.Commands;

public class UploadCommandSettings : CommandSettings
{
    [CommandArgument(0, "<file-paths>")]
    public string[] FilePaths { get; set; } = [];

    [CommandOption("-c|--chunk-size")]
    public int ChunkSizeKb { get; set; } = 1024;

    [CommandOption("-p|--providers")]
    public string Providers { get; set; } = "all";

    [CommandOption("--max-inflight-chunks")]
    public int MaxInFlightChunks { get; set; } = 8;
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
        var resolvedPaths = ExpandPatterns(settings.FilePaths);

        if (resolvedPaths.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No files matched the provided paths/patterns.[/]");
            return 1;
        }

        var options = new UploadOptions
        {
            ChunkSizeBytes = settings.ChunkSizeKb * 1024,
            StorageProviders = ParseProviders(settings.Providers),
            MaxInFlightChunks = settings.MaxInFlightChunks
        };

        BatchUploadResult? batchResult = null;

        try
        {
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var tasks = resolvedPaths.ToDictionary(
                        path => path,
                        path =>
                        {
                            var fi = new FileInfo(path);
                            var estimated = (int)Math.Max(1, Math.Ceiling((double)fi.Length / options.ChunkSizeBytes));
                            return ctx.AddTask(Path.GetFileName(path), maxValue: estimated);
                        });

                    batchResult = await _uploadService.UploadManyAsync(
                        resolvedPaths,
                        options,
                        progressFactory: path => new Progress<(int completed, int total)>(p =>
                        {
                            if (tasks.TryGetValue(path, out var t))
                            {
                                t.MaxValue = p.total;
                                t.Value = p.completed;
                            }
                        }),
                        cancellationToken: cancellationToken);

                    foreach (var t in tasks.Values)
                    {
                        t.Value = t.MaxValue;
                        t.StopTask();
                    }
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Upload failed: {ex.Message}[/]");
            return 1;
        }

        var table = new Table()
            .AddColumn("File")
            .AddColumn("Status")
            .AddColumn("File ID")
            .AddColumn("Size")
            .AddColumn("Chunks");

        foreach (var r in batchResult!.Results)
        {
            if (r.Success)
                table.AddRow(
                    r.FileName,
                    "[green]OK[/]",
                    r.FileManifestId.ToString(),
                    $"{r.OriginalSize:N0} bytes",
                    r.ChunkCount.ToString());
            else
                table.AddRow(
                    r.FileName,
                    "[red]FAILED[/]",
                    "-",
                    "-",
                    r.ErrorMessage ?? "Unknown error");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"[bold]{batchResult.SuccessCount}/{batchResult.TotalCount} file(s) uploaded successfully.[/]");

        return batchResult.AllSucceeded ? 0 : 1;
    }

    private static IReadOnlyList<string> ExpandPatterns(string[] patterns)
    {
        var expanded = new List<string>();
        var globPatterns = new List<string>();

        foreach (var pattern in patterns)
        {
            if (!pattern.Contains('*') && !pattern.Contains('?'))
                expanded.Add(pattern);
            else
                globPatterns.Add(pattern.Replace('\\', '/'));
        }

        if (globPatterns.Count > 0)
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            foreach (var p in globPatterns)
                matcher.AddInclude(p);

            var result = matcher.Execute(
                new DirectoryInfoWrapper(new DirectoryInfo(Directory.GetCurrentDirectory())));
            expanded.AddRange(result.Files.Select(f => Path.GetFullPath(f.Path)));
        }

        return expanded;
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
                "s3" => StorageProviderType.S3,
                _ => throw new ArgumentException($"Unknown provider: {p}")
            })
            .ToArray();
    }
}
