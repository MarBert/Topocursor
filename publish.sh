#!/usr/bin/env bash
# Eseguibile singolo per Linux in ./publish-linux (ARCH: linux-x64 di default, oppure linux-arm64).
# Self-contained: non serve .NET installato sul PC di destinazione.
set -euo pipefail
cd "$(dirname "$0")"
RID="${1:-linux-x64}"
dotnet publish src/Linux/TopoCursor.Linux.csproj -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish-linux
echo "Fatto: publish-linux/topocursor"
