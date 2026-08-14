#!/usr/bin/env bash
#
# Builds the Unity-facing Jitter2.Core assembly and stages it for installation.
#
# Unity cannot compile the snapshot: it fixes game assemblies at C# 9, and the snapshot is written
# in a later language. That limit applies to sources Unity compiles, not to an assembly it loads,
# so the snapshot is compiled here instead and shipped as a managed plugin.
#
# Run this after `sync-jitter2.py` or after editing anything under `Jitter2~/`, then commit the
# result together with the refreshed `jitter2.lock.json`.

set -euo pipefail

PACKAGE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="${PACKAGE_ROOT}/Jitter2~/StandaloneUnity/Jitter2.Core.csproj"
OUTPUT="${PACKAGE_ROOT}/Jitter2~/Prebuilt"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK not found; it is required to build the Unity assembly." >&2
    exit 1
fi

echo "==> applying netstandard2.1 patches"
python3 "${PACKAGE_ROOT}/tools~/patch-jitter2-netstandard.py"

echo "==> building Jitter2.Core (netstandard2.1, Release)"
dotnet build "${PROJECT}" -c Release -v quiet --nologo

BUILD_DIR="${PACKAGE_ROOT}/Jitter2~/StandaloneUnity/bin/Release/netstandard2.1"

if [[ ! -f "${BUILD_DIR}/Jitter2.Core.dll" ]]; then
    echo "build produced no assembly at ${BUILD_DIR}" >&2
    exit 1
fi

# System.Runtime.CompilerServices.Unsafe is not part of netstandard2.1 and Unity does not ship it
# to players, so it travels with the plugin. It is resolved from the local NuGet cache rather than
# copied out of the build folder, because the build folder holds a reference assembly.
UNSAFE_PACKAGE="${HOME}/.nuget/packages/system.runtime.compilerservices.unsafe/6.0.0/lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll"

if [[ ! -f "${UNSAFE_PACKAGE}" ]]; then
    echo "expected dependency not found in the NuGet cache: ${UNSAFE_PACKAGE}" >&2
    exit 1
fi

echo "==> staging into Jitter2~/Prebuilt"
mkdir -p "${OUTPUT}"
cp "${BUILD_DIR}/Jitter2.Core.dll" "${OUTPUT}/Jitter2.Core.dll"
cp "${BUILD_DIR}/Jitter2.Core.xml" "${OUTPUT}/Jitter2.Core.xml"
cp "${UNSAFE_PACKAGE}" "${OUTPUT}/System.Runtime.CompilerServices.Unsafe.dll"

echo
echo "staged assemblies:"
for file in "${OUTPUT}"/*.dll; do
    printf '  %-48s %s\n' "$(basename "${file}")" "$(shasum -a 256 "${file}" | cut -c1-16)…"
done

echo
echo "Next: python3 tools~/sync-jitter2.py --relock   (refreshes jitter2.lock.json)"

