#!/usr/bin/env bash
# Run the portable package tests under .NET, outside Unity.
#
# The dedicated server compiles the same Contracts and ArtifactCodec sources with a
# different compiler and runtime than Unity does, so "green in Unity" is not evidence that
# the server agrees. This script is what CI runs to get that evidence.

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="${script_dir}/../Server~/Tests/DataSakura.JitterPhysics.Server.Tests.csproj"

if [[ ! -f "${project}" ]]; then
  echo "error: ${project} not found" >&2
  exit 1
fi

exec dotnet test "${project}" "$@"

