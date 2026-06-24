# BitScatter

BitScatter is a .NET 10 console application that splits files into chunks, round-robin distributes them across multiple storage backends (Local Filesystem, PostgreSQL, and Amazon S3), and reassembles them with SHA-256 integrity verification.


## Quick Start

### 1. Prerequisites & Services
Ensure you have the .NET 10 SDK and Docker installed.
```bash
make docker-up # Starts PostgreSQL container
make build     # Builds release config
```

### 2. Basic Commands
```bash
# Upload a file (supports multiple files / glob pattern)
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin

# List uploaded files
dotnet run --project src/BitScatter.Cli -- list

# Download a file by its ID
dotnet run --project src/BitScatter.Cli -- download <file-id> /path/to/output.bin

# Delete a file
dotnet run --project src/BitScatter.Cli -- delete <file-id>
```

## Configuration

Settings are loaded from `src/BitScatter.Cli/appsettings.json` or `BITSCATTER_` environment variables.

Example `appsettings.json`:
```json
{
  "BitScatter": {
    "Metadata": "bitscatter.db",
    "FileSystemProviders": [{ "Name": "node1", "Path": "/tmp/node1" }],
    "DatabaseProviders": [{
      "Name": "postgres",
      "ConnectionString": "Host=localhost;Database=bitscatter_chunks;Username=bitscatter;Password=bitscatter"
    }],
    "S3": {
      "Name": "s3",
      "Bucket": "bitscatter-chunks",
      "Region": "us-east-1",
      "AccessKey": "your-access-key",
      "SecretKey": "your-secret-key"
    }
  }
}
```

## Development & Test

- **Run Tests**: `make test` or `make test-watch`
- **Run Benchmarks**: `dotnet run --project benchmarks/BitScatter.Benchmarks -c Release`
- **Run E2E Demo**: `make demo`

## Release

Release artifacts are built into single-file, self-contained executables:
```bash
git tag v1.4.0 && git push origin v1.4.0
make release
```
