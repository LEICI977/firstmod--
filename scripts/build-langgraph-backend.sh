#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PYTHON_BIN="${PYTHON_BIN:-$ROOT/.langgraph-venv/bin/python3}"
if [[ ! -x "$PYTHON_BIN" ]]; then
  PYTHON_BIN="${PYTHON_BIN_FALLBACK:-python3}"
fi

"$PYTHON_BIN" -c 'import PyInstaller' >/dev/null
ARCH="$(uname -m)"
case "$ARCH" in
  arm64|aarch64) PLATFORM="osx-arm64" ;;
  x86_64|amd64) PLATFORM="osx-x64" ;;
  *) echo "Unsupported architecture: $ARCH" >&2; exit 1 ;;
esac

OUTPUT="$ROOT/artifacts/backend/$PLATFORM"
WORK="$ROOT/backend-build-current"
rm -rf "$OUTPUT" "$WORK"
mkdir -p "$OUTPUT" "$WORK"
"$PYTHON_BIN" -m PyInstaller --noconfirm --clean --onedir \
  --name VivantValley.LangGraph \
  --distpath "$WORK/dist" \
  --workpath "$WORK/work" \
  --specpath "$WORK" \
  "$ROOT/langgraph_service/app.py"
cp -R "$WORK/dist/VivantValley.LangGraph/"* "$OUTPUT/"
chmod +x "$OUTPUT/VivantValley.LangGraph"
rm -rf "$WORK"
echo "Bundled LangGraph backend: $OUTPUT"
