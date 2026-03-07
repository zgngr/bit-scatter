# BitScatter

BitScatter is a .NET 10 console application that splits large files into chunks, distributes them across multiple storage providers (file system and/or PostgreSQL), and reassembles them with SHA-256 integrity verification.

## Features

- **Chunked file upload**: Files are split into fixed-size chunks (configurable, default 1024 KB)
- **Parallel batch uploads**: Multiple files are uploaded concurrently using `Parallel.ForEachAsync` (default max concurrency: 4 files)
- **Glob pattern support**: Accepts file glob patterns (e.g., `*.bin`) for batch uploads
- **Multi-provider distribution**: Chunks are round-robin distributed across configured storage providers
- **SHA-256 integrity**: Both individual chunks and the full reconstructed file are verified
- **Retry logic**: Polly-based retry policies with exponential backoff for transient storage failures
- **Metadata persistence**: File manifests and chunk metadata stored in SQLite
- **CLI interface**: Spectre.Console-powered CLI with per-file progress bars and result tables
- **Streaming architecture**: Files are chunked and transferred as streams — no full-file buffering

## Architecture

```
BitScatter.Cli            → CLI entry point (Spectre.Console.Cli, DI wiring, Serilog)
BitScatter.Application    → Business logic, interfaces, DTOs, strategies
BitScatter.Domain         → Entities, enums, domain exceptions
BitScatter.Infrastructure → EF Core, storage providers, repositories, DI extensions
```

Dependencies flow inward: `Cli → Application ← Infrastructure`, with `Domain` at the core.

### Key Abstractions

| Interface | Description |
|---|---|
| `IStorageProvider` | Pluggable chunk storage (FileSystem, Database) |
| `IChunkingStrategy` | Pluggable file splitting strategy (yields `IAsyncEnumerable<ChunkData>`) |
| `IChunkingStrategyFactory` | Creates `IChunkingStrategy` instances via DI |
| `IPlacementStrategy` | Selects which provider receives a given chunk |
| `IChecksumService` | Computes SHA-256 checksums on streams or files |
| `IFileManifestRepository` | Metadata persistence for file manifests and chunk info |
| `IUploadService` | Orchestrates chunking, scattering, and manifest persistence |
| `IDownloadService` | Orchestrates chunk retrieval, integrity verification, and reassembly |
| `IDeleteService` | Deletes file manifests and stored chunks |

### Storage Providers

| Provider | Backend | Retry Policy |
|---|---|---|
| `FileSystemStorageProvider` | Local filesystem (configurable paths) | 3 retries on `IOException`, exponential backoff (200/400/600 ms) |
| `DatabaseStorageProvider` | PostgreSQL via EF Core | 3 retries on transient exceptions, exponential backoff |

### Databases

| Database | Purpose | Default |
|---|---|---|
| SQLite (`BitScatterDbContext`) | File manifests and chunk metadata | `bitscatter.db` |
| PostgreSQL (`ChunkStorageDbContext`) | Raw chunk binary data | Enabled in default `appsettings.json` |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (required for default configuration, which includes PostgreSQL chunk storage)

## Quick Start

### 1. Start PostgreSQL

```bash
make docker-up
```

### 2. Build

```bash
make build
```

### 3. Upload a file

```bash
# Upload a single file (uses all configured providers by default)
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin

# Upload with a custom chunk size (512 KB)
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin --chunk-size 512

# Upload to filesystem providers only
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin --providers filesystem

# Upload to database storage (requires PostgreSQL)
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin --providers database

# Upload with a max in-flight chunk limit
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin --max-inflight-chunks 16

# Upload multiple files in parallel
dotnet run --project src/BitScatter.Cli -- upload file1.bin file2.bin file3.bin

# Upload using a glob pattern
dotnet run --project src/BitScatter.Cli -- upload "/data/*.bin"
```

### 4. List uploaded files

```bash
dotnet run --project src/BitScatter.Cli -- list
```

### 5. Download a file

```bash
dotnet run --project src/BitScatter.Cli -- download <file-id> /path/to/output.bin
```

### 6. Delete a file

```bash
dotnet run --project src/BitScatter.Cli -- delete <file-id>
```

## Configuration

Edit `src/BitScatter.Cli/appsettings.json`:

```json
{
  "BitScatter": {
    "FileSystemProviders": [
      { "Name": "node1", "Path": "/tmp/node1" },
      { "Name": "node2", "Path": "/tmp/node2" },
      { "Name": "node3", "Path": "/tmp/node3" },
      { "Name": "node4", "Path": "/tmp/node4" }
    ],
    "DatabaseProviders": [
      {
        "Name": "database",
        "ConnectionString": "Host=localhost;Port=5432;Database=bitscatter_chunks;Username=bitscatter;Password=bitscatter"
      }
    ]
  },
  "ConnectionStrings": {
    "Metadata": "Data Source=bitscatter.db",
    "ChunkStorage": "Host=localhost;Database=bitscatter_chunks;Username=bitscatter;Password=bitscatter"
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": { "restrictedToMinimumLevel": "Fatal" }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/bitscatter-.log",
          "rollingInterval": "Day",
          "restrictedToMinimumLevel": "Information"
        }
      }
    ]
  }
}
```

`BitScatter:DatabaseProviders` is the preferred way to configure database chunk storage.  
`ConnectionStrings:ChunkStorage` is still supported as a fallback when no database provider entry is present.

Environment variable overrides use the `BITSCATTER_` prefix (double underscore for nesting):

```bash
export BITSCATTER_ConnectionStrings__Metadata="Data Source=bitscatter.db"
export BITSCATTER_ConnectionStrings__ChunkStorage="Host=localhost;Database=bitscatter_chunks;Username=bitscatter;Password=bitscatter"
```

## Running Tests

```bash
make test

# Watch mode (re-runs on file changes)
make test-watch
```
The test suite covers Domain, Application, and Infrastructure layers.

## Running Benchmarks

```bash
dotnet run --project benchmarks/BitScatter.Benchmarks -c Release
```

Benchmarks measure chunking throughput across file sizes (1 MB, 10 MB) and chunk sizes (64 KB, 512 KB, 1 MB) with memory allocation tracking via `[MemoryDiagnoser]`.

## Makefile Targets

| Target | Description |
|---|---|
| `make build` | Build in Release configuration |
| `make test` | Run all tests |
| `make test-watch` | Run Application tests in watch mode |
| `make restore` | Restore NuGet packages |
| `make clean` | Clean build artifacts |
| `make format` | Format code with `dotnet format` |
| `make docker-up` | Start PostgreSQL container |
| `make docker-down` | Stop PostgreSQL container |
| `make docker-logs` | Tail PostgreSQL container logs |
| `make run-list` | List all uploaded files |
| `make run-upload PATTERN=/path/to/file.bin` | Upload one file/path pattern |
| `make run-download ID=<guid> OUTPUT=path` | Download a file by ID |
| `make run-delete ID=<guid>` | Delete a file and all its chunks |
| `make demo` | Run end-to-end upload → download → verify → delete pipeline |
