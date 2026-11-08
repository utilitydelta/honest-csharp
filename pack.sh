#!/usr/bin/env bash
# Pack the analyzer and runtime into a local NuGet feed that other repos consume.
# Default feed is ~/work/.honest-feed so every repo under ~/work can reference it.
set -euo pipefail
cd "$(dirname "$0")"

FEED="${1:-$HOME/work/.honest-feed}"
mkdir -p "$FEED"

dotnet pack src/Honest.Analyzers/Honest.Analyzers.csproj -c Release -o "$FEED" --nologo -v q
dotnet pack src/Honest.Runtime/Honest.Runtime.csproj   -c Release -o "$FEED" --nologo -v q

echo "packed UtilityDelta.Honest.Analyzers + UtilityDelta.Honest.Runtime to $FEED"
