#!/usr/bin/env bash
# Compila e avvia TopoCursor per Linux (X11). Ctrl+C lo chiude in modo pulito.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build src/Linux/TopoCursor.Linux.csproj -c Release --nologo -v q
exec src/Linux/bin/Release/net8.0/topocursor "$@"
