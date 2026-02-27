# BitScatter

BitScatter is a .NET 10 console application that splits large files into chunks, distributes them across multiple storage providers (file system and/or PostgreSQL), and reassembles them with SHA-256 integrity verification.

## Features

- **Chunked file upload**: Files are split into fixed-size chunks (configurable, default 1 MB)
- **Multi-provider distribution**: Chunks are round-robin distributed across configured storage providers
- **SHA-256 integrity**: Both individual chunks and the full reconstructed file are verified
- **Retry logic**: Polly-based retry policies for transient storage failures
- **Metadata persistence**: File manifests and chunk metadata stored in SQLite
- **CLI interface**: Spectre.Console-powered CLI with progress indicators

## Architecture

```
BitScatter.Cli            → CLI entry point (Spectre.Console.Cli)
BitScatter.Application    → Business logic, interfaces, DTOs
BitScatter.Domain         → Entities, enums, domain exceptions
BitScatter.Infrastructure → EF Core, storage providers, repositories
```

### Key Abstractions

| Interface | Description |
|---|---|
| `IStorageProvider` | Pluggable chunk storage (FileSystem, Database) |
| `IChunkingStrategy` | Pluggable file splitting strategy |
| `IFileManifestRepository` | Metadata persistence |
| `IUploadService` | Orchestrates chunking + distribution |
| `IDownloadService` | Orchestrates retrieval + reassembly |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (optional, for PostgreSQL storage)

## Quick Start

### 1. Start PostgreSQL (optional)

```bash
make docker-up
```

### 2. Build

```bash
make build
```

### 3. Upload a file

```bash
# Upload using filesystem storage (default)
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin

# Upload with custom chunk size (512 KB) and multiple providers
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin --chunk-size 512 --providers filesystem

# Upload to database storage (requires PostgreSQL)
dotnet run --project src/BitScatter.Cli -- upload /path/to/file.bin --providers database
```

### 4. List uploaded files

```bash
dotnet run --project src/BitScatter.Cli -- list
```

### 5. Download a file

```bash
dotnet run --project src/BitScatter.Cli -- download <file-id> /path/to/output.bin
```

## Configuration

Edit `src/BitScatter.Cli/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Metadata": "Data Source=bitscatter.db",
    "ChunkStorage": "Host=localhost;Database=bitscatter_chunks;Username=bitscatter;Password=bitscatter"
  },
  "Storage": {
    "FileSystemPath": "chunks"
  }
}
```

Environment variable overrides use the `BITSCATTER_` prefix:

```bash
export BITSCATTER_ConnectionStrings__ChunkStorage="Host=localhost;..."
```

## Running Tests

```bash
make test
```

## Makefile Targets

| Target | Description |
|---|---|
| `make build` | Build in Release configuration |
| `make test` | Run all tests |
| `make docker-up` | Start PostgreSQL container |
| `make docker-down` | Stop PostgreSQL container |
| `make run-list` | List uploaded files |
| `make run-upload FILE=path` | Upload a file |
| `make run-download ID=<guid> OUTPUT=path` | Download a file |
