.PHONY: build test run-upload run-upload-multi run-upload-glob run-list run-delete run-download docker-up docker-down docker-logs docker-logs-postgres docker-logs-localstack docker-init-s3 restore clean demo format release release-one release-smoke release-validate-version

SOLUTION := BitScatter.slnx
CLI_PROJECT := src/BitScatter.Cli/BitScatter.Cli.csproj
POSTGRES_SERVICE := postgres
LOCALSTACK_SERVICE := localstack
LOCALSTACK_CONTAINER := bitscatter-localstack
S3_BUCKET := bitscatter-chunks
RIDS := linux-x64 osx-arm64 win-x64
VERSION := $(shell git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$$' | head -n 1)
RELEASE_DIR := artifacts/release/$(VERSION)

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

release-validate-version:
	@set -eu; \
	version_tags="$$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$$' || true)"; \
	count="$$(printf '%s\n' "$$version_tags" | sed '/^$$/d' | wc -l | tr -d ' ')"; \
	if [ "$$count" -eq 0 ]; then \
		echo "ERROR: HEAD must be tagged with a SemVer tag like v1.2.3 or v1.2.3-rc.1."; \
		echo "Run: git tag vX.Y.Z && git push origin vX.Y.Z"; \
		exit 1; \
	fi; \
	if [ "$$count" -gt 1 ]; then \
		echo "ERROR: Multiple SemVer tags found at HEAD. Keep exactly one release tag."; \
		printf '%s\n' "$$version_tags"; \
		exit 1; \
	fi; \
	echo "Release version: $$version_tags"

release: release-validate-version
	@set -eu; \
	version_tag="$(VERSION)"; \
	version="$${version_tag#v}"; \
	version_base="$${version%%-*}"; \
	major="$${version_base%%.*}"; \
	minor="$${version_base#*.}"; \
	minor="$${minor%%.*}"; \
	patch="$${version_base##*.}"; \
	assembly_version="$$major.$$minor.0.0"; \
	file_version="$$major.$$minor.$$patch.0"; \
	info_version="$$version_tag+$$(git rev-parse --short HEAD)"; \
	mkdir -p "$(RELEASE_DIR)"; \
	for rid in $(RIDS); do \
		out_dir="$(RELEASE_DIR)/$$rid"; \
		echo "Publishing $$rid -> $$out_dir"; \
		dotnet publish "$(CLI_PROJECT)" --configuration Release --runtime "$$rid" --self-contained true --output "$$out_dir" \
			-p:PublishSingleFile=true \
			-p:DebugType=None \
			-p:DebugSymbols=false \
			-p:Version="$$version" \
			-p:AssemblyVersion="$$assembly_version" \
			-p:FileVersion="$$file_version" \
			-p:InformationalVersion="$$info_version"; \
		if [ -f "$$out_dir/appsettings.json" ]; then \
			mv "$$out_dir/appsettings.json" "$$out_dir/appsettings.example.json"; \
		else \
			cp src/BitScatter.Cli/appsettings.json "$$out_dir/appsettings.example.json"; \
		fi; \
	done; \
	$(MAKE) release-smoke

release-one: release-validate-version
	@set -eu; \
	if [ -z "$(RID)" ]; then \
		echo "ERROR: RID is required. Example: make release-one RID=linux-x64"; \
		exit 1; \
	fi; \
	version_tag="$(VERSION)"; \
	version="$${version_tag#v}"; \
	version_base="$${version%%-*}"; \
	major="$${version_base%%.*}"; \
	minor="$${version_base#*.}"; \
	minor="$${minor%%.*}"; \
	patch="$${version_base##*.}"; \
	assembly_version="$$major.$$minor.0.0"; \
	file_version="$$major.$$minor.$$patch.0"; \
	info_version="$$version_tag+$$(git rev-parse --short HEAD)"; \
	out_dir="$(RELEASE_DIR)/$(RID)"; \
	echo "Publishing $(RID) -> $$out_dir"; \
	dotnet publish "$(CLI_PROJECT)" --configuration Release --runtime "$(RID)" --self-contained true --output "$$out_dir" \
		-p:PublishSingleFile=true \
		-p:DebugType=None \
		-p:DebugSymbols=false \
		-p:Version="$$version" \
		-p:AssemblyVersion="$$assembly_version" \
		-p:FileVersion="$$file_version" \
		-p:InformationalVersion="$$info_version"; \
	if [ -f "$$out_dir/appsettings.json" ]; then \
		mv "$$out_dir/appsettings.json" "$$out_dir/appsettings.example.json"; \
	else \
		cp src/BitScatter.Cli/appsettings.json "$$out_dir/appsettings.example.json"; \
	fi; \
	$(MAKE) release-smoke RID="$(RID)"

release-smoke: release-validate-version
	@set -eu; \
	if [ -n "$(RID)" ]; then \
		rid="$(RID)"; \
	else \
		os="$$(uname -s)"; \
		arch="$$(uname -m)"; \
		case "$$os/$$arch" in \
			Linux/x86_64) rid="linux-x64" ;; \
			Darwin/arm64) rid="osx-arm64" ;; \
			*) \
				echo "WARNING: No host-compatible RID mapping for $$os/$$arch. Skipping smoke test."; \
				exit 0 ;; \
		esac; \
	fi; \
	case "$$rid" in \
		win-x64) exe="$(RELEASE_DIR)/$$rid/bitscatter.exe" ;; \
		*) exe="$(RELEASE_DIR)/$$rid/bitscatter" ;; \
	esac; \
	if [ ! -f "$$exe" ]; then \
		echo "ERROR: Smoke binary not found: $$exe"; \
		echo "Run release or release-one first."; \
		exit 1; \
	fi; \
	case "$$rid" in \
		linux-x64) [ "$$(uname -s)" = "Linux" ] || { echo "WARNING: Cannot execute linux-x64 binary on this host. Skipping."; exit 0; } ;; \
		osx-arm64) [ "$$(uname -s)" = "Darwin" ] && [ "$$(uname -m)" = "arm64" ] || { echo "WARNING: Cannot execute osx-arm64 binary on this host. Skipping."; exit 0; } ;; \
		win-x64) echo "WARNING: Cannot execute win-x64 binary in this shell environment. Skipping."; exit 0 ;; \
	esac; \
	echo "Smoke testing $$exe"; \
	"$$exe" --help >/dev/null; \
	echo "Smoke test passed for $$rid"
