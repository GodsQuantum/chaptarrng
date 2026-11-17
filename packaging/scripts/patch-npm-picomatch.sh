#!/usr/bin/env bash
set -euo pipefail

# npm 12.2.2 bundles its own copy, outside Yarn's dependency graph.
target="$(npm root --global)/npm/node_modules/tinyglobby/node_modules/picomatch"
node -e 'const p = require(process.argv[1]); if (p.name !== "picomatch" || p.version !== "4.0.5") process.exit(1)' "$target/package.json"

work="$(mktemp -d)"
trap 'rm -r -- "$work"' EXIT
curl --fail --location --silent --show-error \
  https://registry.npmjs.org/@ryotsu/picomatch/-/picomatch-4.0.7-a.tgz \
  -o "$work/picomatch.tgz"
printf '%s  %s\n' \
  26ba0114499a7a30bbb331927ea3a11754191cb2af5f6c13035f9f5b27e4a72dc18f375028d3cb29ad85b6078a5540f2e7f50f50d91711e08727965104c2d354 \
  "$work/picomatch.tgz" | sha512sum -c -
tar -xzf "$work/picomatch.tgz" -C "$work"
node -e 'const p = require(process.argv[1]); if (p.name !== "@ryotsu/picomatch" || p.version !== "4.0.7-a") process.exit(1)' "$work/package/package.json"

find "$target" -mindepth 1 -delete
cp -a "$work/package/." "$target"
