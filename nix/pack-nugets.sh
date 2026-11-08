# Packed by the flake app `pack`. DOTNET_ROOT is injected by flake.nix.
set -euo pipefail

if [[ -z ${DOTNET_ROOT:-} || ! -x ${DOTNET_ROOT}/dotnet ]]; then
  echo "error: DOTNET_ROOT is unset. From the Fable repo root run: nix run .#pack" >&2
  exit 1
fi
export PATH="${DOTNET_ROOT}:${PATH}"

root=${FABLE_ROOT:-$PWD}
cd "$root"

if [[ ! -f src/Fable.Compiler/Fable.Compiler.fsproj ]]; then
  echo "error: src/Fable.Compiler/Fable.Compiler.fsproj not found. Run from the Fable repository root." >&2
  exit 1
fi

out=${1:-${NUPKG_OUT:-artifacts/nuget}}
mkdir -p "$out"
out=$(cd "$out" && pwd)

base_version=5.19.0
changelog=src/Fable.Compiler/CHANGELOG.md
if [[ -f $changelog ]]; then
  parsed=$(grep -E '^## [0-9]+\.[0-9]+\.[0-9]+( |$)' "$changelog" | head -n 1 | sed -E 's/^## ([0-9]+\.[0-9]+\.[0-9]+).*/\1/' || true)
  if [[ -n ${parsed:-} ]]; then
    base_version=$parsed
  fi
fi

if short_sha=$(git rev-parse --short=12 HEAD 2>/dev/null); then
  :
else
  short_sha=nogit
fi
full_sha=$(git rev-parse HEAD 2>/dev/null || echo "")

if [[ -n ${FABLE_PACKAGE_VERSION:-} ]]; then
  version=$FABLE_PACKAGE_VERSION
else
  run_number=${GITHUB_RUN_NUMBER:-0}
  run_attempt=${GITHUB_RUN_ATTEMPT:-0}
  version="${base_version}-wasmgc.${run_number}.${run_attempt}.sha${short_sha}"
fi

if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]]; then
  echo "error: invalid NuGet version: $version" >&2
  exit 1
fi

repo_slug=${GITHUB_REPOSITORY:-aiqs4/Fable}
repo_url="https://github.com/${repo_slug}"

projects=(
  src/Fable.AST/Fable.AST.fsproj
  src/Fable.Transforms/Global/Fable.Transforms.Global.fsproj
)
if [[ -f src/Fable.Transforms/Rust/AST/Rust.AST.fsproj ]]; then
  # Fable.Transforms takes a package dependency on this project.
  projects+=(src/Fable.Transforms/Rust/AST/Rust.AST.fsproj)
fi
projects+=(
  src/Fable.Transforms/Fable.Transforms.fsproj
  src/Fable.Compiler/Fable.Compiler.fsproj
)

missing=0
for proj in "${projects[@]}"; do
  if [[ ! -f $proj ]]; then
    echo "missing project: $proj" >&2
    missing=1
    continue
  fi
  proj_dir=$(dirname "$proj")
  includes=$(mktemp)
  python3 - "$proj" "$includes" <<'PY'
import re, sys
text = open(sys.argv[1], encoding="utf-8").read()
text = re.sub(r"<!--.*?-->", "", text, flags=re.S)
with open(sys.argv[2], "w", encoding="utf-8") as out:
    for match in re.finditer(r'Include="([^"]+\.(?:fs|fsi|fsproj))"', text):
        out.write(match.group(1) + "\n")
PY
  while IFS= read -r inc; do
    [[ -z $inc ]] && continue
    if [[ ! -e $proj_dir/$inc ]]; then
      printf 'missing source: %s (from %s)\n' "$proj_dir/$inc" "$proj" >&2
      missing=1
    fi
  done <"$includes"
  rm -f "$includes"
done
if [[ $missing -ne 0 ]]; then
  echo "error: this checkout cannot be packed. Commit the missing sources (feature/wasmgc-backend is missing the language-backend files Fable.Transforms still compiles)." >&2
  exit 1
fi

common=(
  -c Release
  --nologo
  -o "$out"
  -p:ContinuousIntegrationBuild=true
  -p:EasyBuildPackageReleaseNotes_DisableSetVersion=true
  -p:Version="$version"
  -p:PackageVersion="$version"
  -p:IncludeSymbols=false
  -p:SatelliteResourceLanguages=en
  -p:RepositoryUrl="$repo_url"
  -p:RepositoryType=git
  -p:RepositoryBranch=feature/wasmgc-backend
  -p:PackageProjectUrl="$repo_url"
)
if [[ -n $full_sha ]]; then
  common+=(-p:RepositoryCommit="$full_sha")
fi

for proj in "${projects[@]}"; do
  echo "packing $proj -> $version"
  dotnet pack "$proj" "${common[@]}"
done

expected=(
  Fable.AST
  Fable.Transforms.Global
  Fable.Transforms
  Fable.Compiler
)
for id in "${expected[@]}"; do
  if [[ ! -f $out/${id}.${version}.nupkg ]]; then
    echo "error: expected package was not produced: ${id}.${version}.nupkg" >&2
    exit 1
  fi
done

printf '%s\n' "$version" >"$out/version.txt"

cat >"$out/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="fable-wasmgc" value="./nugets" />
  </packageSources>
</configuration>
EOF

cat >"$out/README.txt" <<EOF
Fable WasmGC NuGet ${version}
Source: ${repo_url} @ ${full_sha:-unknown} (feature/wasmgc-backend)

Download
  gh release download wasmgc-nuget --repo ${repo_slug} --pattern '*.nupkg' --dir nugets
  or download fable-wasmgc-nugets.zip from the wasmgc-nuget release and unzip it into ./nugets

Restore (nuget.config above points a local source at ./nugets):

  <ItemGroup>
    <PackageReference Include="Fable.AST" Version="${version}" />
    <PackageReference Include="Fable.Transforms" Version="${version}" />
    <PackageReference Include="Fable.Transforms.Global" Version="${version}" />
    <PackageReference Include="Fable.Compiler" Version="${version}" />
  </ItemGroup>

Replace ProjectReferences such as:

  ../../vendor/Fable/src/Fable.AST/Fable.AST.fsproj
  ../../vendor/Fable/src/Fable.Transforms/Fable.Transforms.fsproj
  ../../vendor/Fable/src/Fable.Transforms/Global/Fable.Transforms.Global.fsproj
  ../../vendor/Fable/src/Fable.Compiler/Fable.Compiler.fsproj

GitHub release downloads are not a NuGet feed. Use the folder source.
EOF

cp "$out/README.txt" "$out/RELEASE.md"

shopt -s nullglob
nupkgs=("$out"/*.nupkg)
if ((${#nupkgs[@]} == 0)); then
  echo "error: no nupkg files in $out" >&2
  exit 1
fi
rm -f "$out/fable-wasmgc-nugets.zip"
(
  cd "$out"
  zip -q fable-wasmgc-nugets.zip version.txt README.txt nuget.config RELEASE.md ./*.nupkg
)

echo "packed ${version} into ${out}"
