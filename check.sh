#!/usr/bin/env bash
# The single gate. CI and agents run this; nothing else.
# If it exits 0, the tree is in policy.
set -euo pipefail
cd "$(dirname "$0")"

echo "==> build (analyzer rules break the demo build)"
dotnet build Honest.slnx --nologo -v q

echo "==> test (the analyzer is itself tested)"
dotnet test Honest.slnx --nologo -v q

echo "==> OK"
