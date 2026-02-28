#!/usr/bin/env bash
# BitScatter E2E demo: upload → list → download → verify → delete → cleanup
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI="dotnet run --project $REPO_ROOT/src/BitScatter.Cli/BitScatter.Cli.csproj --"

# Pre-initialize so the EXIT trap is always safe, even if assignments haven't run yet
DEMO_FILE="" DL_DIR=""
FILE_ID=""

cleanup() {
    # Delete uploaded file still in storage (no-op if already deleted or never uploaded)
    [ -n "$FILE_ID" ] && $CLI delete "$FILE_ID" 2>/dev/null || true
    [ -n "$DEMO_FILE" ] && rm -f "$DEMO_FILE"
    [ -n "$DL_DIR" ]   && rm -rf "$DL_DIR"
}
trap cleanup EXIT

# No .bin suffix: avoids mkstemps() on macOS, uses the simpler mkstemp() path
DEMO_FILE=$(mktemp /tmp/bitscatter-demo-XXXXXX)
DL_DIR=$(mktemp -d /tmp/bitscatter-dl-XXXXXX)

# ── Step 1: Create a 3 MB random test file ────────────────────────────────────
dd if=/dev/urandom of="$DEMO_FILE" bs=1M count=3 2>/dev/null
ORIGINAL_SHA=$(shasum -a 256 "$DEMO_FILE" | awk '{print $1}')
echo ""
echo "[1/6] Created test file: $(basename "$DEMO_FILE")  (SHA256: $ORIGINAL_SHA)"

# ── Step 2: Upload ────────────────────────────────────────────────────────────
echo ""
echo "[2/6] Uploading with 512 KB chunks (filesystem providers only)..."
UPLOAD_OUTPUT=$($CLI upload "$DEMO_FILE" --chunk-size 512 --providers filesystem 2>/dev/null)
echo "$UPLOAD_OUTPUT"

FILE_ID=$(echo "$UPLOAD_OUTPUT" | grep -oE '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}' | head -1)
if [ -z "$FILE_ID" ]; then
    echo "ERROR: Could not extract File ID from upload output." >&2
    exit 1
fi
echo "      File ID: $FILE_ID"

# ── Step 3: List ──────────────────────────────────────────────────────────────
echo ""
echo "[3/6] Listing all stored files..."
$CLI list 2>/dev/null

# ── Step 4: Download ──────────────────────────────────────────────────────────
DOWNLOAD_FILE="$DL_DIR/$FILE_ID"
echo ""
echo "[4/6] Downloading to $(basename "$DOWNLOAD_FILE")..."
$CLI download "$FILE_ID" "$DOWNLOAD_FILE" 2>/dev/null

# ── Step 5: Verify checksum ───────────────────────────────────────────────────
DOWNLOAD_SHA=$(shasum -a 256 "$DOWNLOAD_FILE" | awk '{print $1}')
echo ""
echo "[5/6] Verifying integrity..."
echo "      Original  : $ORIGINAL_SHA"
echo "      Downloaded: $DOWNLOAD_SHA"
if [ "$ORIGINAL_SHA" != "$DOWNLOAD_SHA" ]; then
    echo "ERROR: Checksum mismatch — files differ!" >&2
    exit 1
fi
echo "      Checksums match!"

# ── Step 6: Delete ────────────────────────────────────────────────────────────
echo ""
echo "[6/6] Deleting file $FILE_ID from storage..."
$CLI delete "$FILE_ID" 2>/dev/null
# Clear so the EXIT trap doesn't attempt a second delete
FILE_ID=""

echo ""
echo "Demo complete. System is working end-to-end."
