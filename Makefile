.PHONY: build test run-upload run-upload-multi run-upload-glob run-list run-delete run-download docker-up docker-down docker-logs docker-logs-postgres docker-logs-localstack docker-init-s3 restore clean demo format

SOLUTION := BitScatter.slnx
CLI_PROJECT := src/BitScatter.Cli/BitScatter.Cli.csproj
POSTGRES_SERVICE := postgres
LOCALSTACK_SERVICE := localstack
LOCALSTACK_CONTAINER := bitscatter-localstack
S3_BUCKET := bitscatter-chunks

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
	dotnet run --project $(CLI_PROJECT) -- upload $(PATTERN)

run-download:
	dotnet run --project $(CLI_PROJECT) -- download $(ID) $(OUTPUT)

run-list:
	dotnet run --project $(CLI_PROJECT) -- list

run-delete:
	dotnet run --project $(CLI_PROJECT) -- delete $(ID)

docker-up:
	docker compose up -d
	@echo "Waiting for PostgreSQL to be ready..."
	@sleep 3
	@echo "Waiting for LocalStack (S3) to be ready..."
	@sleep 3
	@$(MAKE) docker-init-s3

docker-down:
	docker compose down

docker-logs:
	docker compose logs -f $(POSTGRES_SERVICE) $(LOCALSTACK_SERVICE)

docker-logs-postgres:
	docker compose logs -f $(POSTGRES_SERVICE)

docker-logs-localstack:
	docker compose logs -f $(LOCALSTACK_SERVICE)

docker-init-s3:
	@echo "Ensuring LocalStack bucket exists: $(S3_BUCKET)"
	@docker exec $(LOCALSTACK_CONTAINER) sh -lc "awslocal s3api head-bucket --bucket $(S3_BUCKET) >/dev/null 2>&1 || awslocal s3api create-bucket --bucket $(S3_BUCKET)"

demo:
	@bash scripts/demo.sh

format:
	dotnet format $(SOLUTION)
