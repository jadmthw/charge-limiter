#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
dotnet publish "$ROOT/ChargeLimiter/ChargeLimiter.csproj" \
  -c Release \
  -r win-arm64 \
  --self-contained true \
  -o "$ROOT/publish/win-arm64"
echo "Published: $ROOT/publish/win-arm64/ChargeLimiter.exe"
