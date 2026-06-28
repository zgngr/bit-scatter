# BitScatter

BitScatter is a .NET 10 console application that splits files into chunks, round-robin distributes them across multiple storage backends (Local Filesystem, PostgreSQL, and Amazon S3), and reassembles them with SHA-256 integrity verification.

## Features

- **End-to-End Client-Side Encryption**: Encrypts chunks using AES-256-GCM before uploading.
- **Adaptive Per-Chunk Compression**: Compresses chunks with Brotli before encryption, keeping the compressed payload only if it shrinks by 5% or more (prevents overhead on incompressible files).
- **Zero-Knowledge Key Encapsulation**: Derives a Key Encryption Key (KEK) using PBKDF2 (100,000 iterations) from your password to encrypt a unique random File Encryption Key (FEK) stored in metadata.
- **Storage Key Obfuscation**: Randomizes storage paths (e.g., `chunks/<guid>`) to prevent remote storage providers from associating chunks with files or indices.
- **Chunked file upload**: Splits files into fixed-size chunks (default 1024 KB) and transfers them as streams.
- **Parallel batch uploads**: Concurrently uploads files using `Parallel.ForEachAsync`.

## Quick Start

### 1. Prerequisites & Services
Ensure you have the .NET 10 SDK and Docker installed.
```bash
make docker-up # Starts PostgreSQL container
make build     # Builds release config
```

### 2. Basic Commands
```bash
# Upload a file
dotnet run --project src/apps/BitScatter.Cli -- upload /path/to/file.bin

# List uploaded files
dotnet run --project src/apps/BitScatter.Cli -- list

# Download a file by its ID
dotnet run --project src/apps/BitScatter.Cli -- download <file-id> /path/to/output.bin

# Delete a file
dotnet run --project src/apps/BitScatter.Cli -- delete <file-id>
```

### 3. Compression, Encryption & Obfuscation Commands
```bash
# Upload a file with Brotli compression
dotnet run --project src/apps/BitScatter.Cli -- upload /path/to/file.bin --compress

# Upload a file with password encryption and randomized storage keys
dotnet run --project src/apps/BitScatter.Cli -- upload /path/to/file.bin --password mypassword123 --obfuscate-keys

# Upload a file with both compression and encryption (compress -> encrypt -> store)
dotnet run --project src/apps/BitScatter.Cli -- upload /path/to/file.bin --compress --password mypassword123

# Download an encrypted file (you will be prompted securely if --password is omitted; decompression is fully automatic)
dotnet run --project src/apps/BitScatter.Cli -- download <file-id> /path/to/output.bin --password mypassword123
```

## Configuration

Settings are loaded from `src/apps/BitScatter.Cli/appsettings.json` or `BITSCATTER_` environment variables.

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
