#!/usr/bin/env bash
# Builds and starts TopoCursor for Linux (X11). Ctrl+C closes it cleanly.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build src/Linux/TopoCursor.Linux.csproj -c Release --nologo -v q
exec src/Linux/bin/Release/net8.0/topocursor "$@"
