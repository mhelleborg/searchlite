#!/bin/bash
# SessionStart hook: ensure the .NET 10 SDK is available for Claude Code on the web.
#
# SearchLite multi-targets net8.0;net9.0;net10.0, so building and testing requires
# the .NET 10 SDK. Remote sessions start from a clean container without it, so
# install it here before the agent runs. On Ubuntu 24.04 the SDK ships in the
# standard archives, so a plain apt install works without adding the Microsoft
# package feed.
set -euo pipefail

# Only run in Claude Code on the web (remote) environments. Local machines are
# expected to have their own SDK installed.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Idempotent: nothing to do if a .NET 10 SDK is already present.
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "[session-start] .NET 10 SDK already present: $(dotnet --version)"
  exit 0
fi

SUDO=""
if [ "$(id -u)" -ne 0 ]; then
  SUDO="sudo"
fi

echo "[session-start] Installing .NET 10 SDK (dotnet-sdk-10.0) via apt..."
export DEBIAN_FRONTEND=noninteractive
$SUDO apt-get update
$SUDO apt-get install -y dotnet-sdk-10.0

echo "[session-start] Installed .NET SDK $(dotnet --version)"
