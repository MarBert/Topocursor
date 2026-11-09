#!/usr/bin/env bash
# Single Linux executable in ./publish-linux (RID: linux-x64 by default, or linux-arm64).
# Self-contained: .NET doesn't need to be installed on the target machine.
set -euo pipefail
cd "$(dirname "$0")"
RID="${1:-linux-x64}"
dotnet publish src/Linux/TopoCursor.Linux.csproj -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish-linux
echo "Done: publish-linux/topocursor"
