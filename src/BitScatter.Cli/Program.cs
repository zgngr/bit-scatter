using BitScatter.Cli;
using BitScatter.Cli.Commands;
using BitScatter.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console.Cli;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("BITSCATTER_")
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/bitscatter-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var services = new ServiceCollection();
services.AddLogging(lb => lb.AddSerilog(Log.Logger, dispose: true));
services.AddBitScatterInfrastructure(configuration);
services.AddSingleton<IServiceProvider>(sp => sp);

var serviceProvider = services.BuildServiceProvider();

await serviceProvider.MigrateAsync();

var app = new CommandApp(new TypeRegistrar(services));
app.Configure(config =>
{
    config.SetApplicationName("bitscatter");
    config.PropagateExceptions();

    config.AddCommand<UploadCommand>("upload")
        .WithDescription("Upload a file by splitting it into chunks across storage providers")
        .WithExample("upload", "./largefile.bin")
        .WithExample("upload", "./largefile.bin", "--chunk-size", "512", "--providers", "filesystem");

    config.AddCommand<DownloadCommand>("download")
        .WithDescription("Download and reassemble a file from its chunks")
        .WithExample("download", "<file-id>", "./output.bin");

    config.AddCommand<ListCommand>("list")
        .WithDescription("List all uploaded files");
});

try
{
    return app.Run(args);
}
finally
{
    await Log.CloseAndFlushAsync();
}
