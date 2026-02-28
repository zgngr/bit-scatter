# CLAUDE.md

## Project Overview
BitScatter is a .NET Console Application that automatically splits large files into small pieces (chunks), distributes these pieces across different storage providers (e.g., local file system, PostgreSQL), and reassembles them while verifying integrity using SHA-256 checksums at both chunk and file levels.

- The system must be able to split single and multiple files into chunks.
- All chunks must be distributed to different IStorageProvider implementations (e.g., FileSystemStorageProvider, DatabaseStorageProvider, etc.).
- Chunk information and associated metadata must be permanently stored in a database. (e.g., SQLite)
- The merging process must retrieve all chunks from their respective storage, reconstruct the original file, and perform checksum verification.

## Key Workflows
- **Uploading**: Reading a file, calculating size/checksum, selecting a chunking strategy, splitting the file into chunks, saving chunks across storage providers, and saving the overall file manifest metadata.
- **Downloading**: Retrieving file metadata, locating and fetching chunks across providers, verifying chunk integrity, reassembling the final stream, verifying the final file checksum, and saving to the output path.
- **Deleting**: Fetching the file manifest, deleting every chunk from its storage provider, then removing the manifest from the database.

## E2E Demo

Run the end-to-end demo to verify the full upload → download → delete pipeline:

```bash
make demo
```

The script (`scripts/demo.sh`):
1. Creates a 3 MB random binary test file
2. Uploads it with 512 KB chunks (filesystem providers)
3. Lists all stored files
4. Downloads the file to a temp path
5. Verifies the SHA-256 checksum matches the original
6. Deletes the file and all its chunks from storage
7. Cleans up all temporary files automatically on exit (even on failure)

> After any significant development, run `make demo` alongside `make test` to confirm E2E integrity.


## Tech Stack

- **Language/Framework**: C#, .NET 10.0
- **IoC Container**: Microsoft.Extensions.DependencyInjection
- **CLI Framework**: Spectre.Console
- **Database/Storage**: PostgreSQL (via Docker), Local File System
- **Testing**: xUnit, Moq
- **Benchmarking**: BenchmarkDotNet

## Important

- Always make sure tests are passing
- Always work under a git branch
- Always keep README.md file up-to-date

## Project Structure
```

bit-scatter/                              <-- The root of your Git repository
├── .vscode/                              # (Optional) IDE specific settings
├── .editorconfig                         # Enforces consistent coding styles
├── .gitignore                            # Ignores bin/, obj/, etc.
├── docker-compose.yml                    # Local infrastructure (Db, Redis, etc.)
├── Dockerfile                            # (Optional) How to containerize the app.
├── global.json                           # (Optional) Pins the exact .NET SDK version to use
├── Makefile                              <-- THE MAKEFILE (Build automation shortcuts)
├── BitScatter.sln                        <-- The Solution file linking everything
├── README.md                             <-- Project documentation
│
├── src/                                  # Source code
│   ├── BitScatter.Cli/
│   │   ├── BitScatter.Cli.csproj
│   │   ├── Program.cs              # Setup DI, Config, Logging, and run the app
│   │   ├── appsettings.json
│   │   └── Commands/               # Optional: CLI command handlers (e.g., using Spectre.Console)
│   │
│   ├── BitScatter.Application/
│   │   ├── BitScatter.Application.csproj
│   │   ├── Interfaces/                         # Interfaces for Infrastructure (e.g., IUserRepository)
│   │   ├── Services/                           # Business logic orchestration
│   │   └── DTOs/                               # Data Transfer Objects
│   │
│   ├── BitScatter.Domain/
│   │   ├── BitScatter.Domain.csproj
│   │   ├── Entities/                   # Core business models (e.g., User, Order)
│   │   ├── Enums/
│   │   └── Exceptions/                 # Domain-specific exceptions
│   │
│   └── BitScatter.Infrastructure/
│       ├── BitScatter.Infrastructure.csproj
│       ├── Data/                           #  Database Context (e.g., Entity Framework Core)
│       ├── Repositories/                   # Implementation of Application interfaces
│       └── FileSystem/                     # Physical file interactions
│
│
├── tests/                                # Test projects
│   ├── BitScatter.Application.Tests/  
│   ├── BitScatter.Domain.Tests/
│   └── BitScatter.Infrastructure.Tests/
│
├── benchmarks/                           # Performance benchmarks
│   └── BitScatter.Benchmarks/
│
└── scripts/                              
    ├── deploy.sh
    └── seed-database.sql
```

## Coding Conventions & Guidelines
1. **Performance & Memory**: 
   - Optimize for large file handling.
   - Use streaming (`Stream`, `IAsyncEnumerable`) over buffering entire files into memory. 
   - Ensure streams are correctly tracked and disposed *only* when no longer needed to prevent data loss or memory leaks.
   - Avoid duplicate reading of files into memory (e.g., during upload and checksum calculation).
2. **Resilience**:
   - Implement retry logic and fallback mechanisms for inaccessible or failed storage providers.
   - Ensure operations are safe and predictable.
3. **Integrity**:
   - Always verify SHA-256 checksums on downloads and uploads. Integrity is a core system feature.
4. **Maintenance**:
   - Keep chunking strategies configurable and extensible. Avoid hardcoding strategy selection logic.
   - Keep Dependency Injection configuration clean and well-organized.
   - Ensure all configuration can be dynamically overridden (e.g., via environment variables).
5. **Testing**:
   - Write comprehensive tests for core domain logic, strategies, and infrastructure services.
   - Consider performance implications and benchmark significant changes.

## Coding Style
- All components must be abstracted based on interfaces to ensure they are testable.
- Logging must be performed during every file and chunk operation (e.g., ILogger, Serilog).
- Coding must comply with .NET C# naming conventions, OOP principles, and SOLID structures.
- The project's README file must include explanatory notes on how to run the application, architectural choices, and any extra features.

## Boundaries
- Never ever delete files with rm or similar commands