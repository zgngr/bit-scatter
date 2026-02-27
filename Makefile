.PHONY: build test run-upload run-list docker-up docker-down restore clean

SOLUTION := BitScatter.slnx
CLI_PROJECT := src/BitScatter.Cli/BitScatter.Cli.csproj

build:
	dotnet build $(SOLUTION) --configuration Release

test:
	dotnet test $(SOLUTION) --configuration Release --no-build --logger "console;verbosity=detailed"

test-watch:
	dotnet watch test --project tests/BitScatter.Application.Tests/BitScatter.Application.Tests.csproj

restore:
	dotnet restore $(SOLUTION)

clean:
	dotnet clean $(SOLUTION)

run-upload:
	dotnet run --project $(CLI_PROJECT) -- upload $(FILE)

run-download:
	dotnet run --project $(CLI_PROJECT) -- download $(ID) $(OUTPUT)

run-list:
	dotnet run --project $(CLI_PROJECT) -- list

docker-up:
	docker-compose up -d
	@echo "Waiting for PostgreSQL to be ready..."
	@sleep 3

docker-down:
	docker-compose down

docker-logs:
	docker-compose logs -f postgres

format:
	dotnet format $(SOLUTION)
