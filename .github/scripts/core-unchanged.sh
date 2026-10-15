#!/bin/sh
# Refuses a pitboard-core on crates.io whose source or manifests differ from the tree's.
set -eu

if [ "$#" -ne 2 ]; then
  echo "usage: core-unchanged.sh <published .crate> <packaged .crate>" >&2
  exit 2
fi
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

unpacked() {
  mkdir "$work/$2"
  tar -xzf "$1" -C "$work/$2"
  set -- "$1" "$work/$2"/*/
  if [ "$#" -ne 2 ] || [ ! -f "$2/Cargo.toml.orig" ]; then
    echo "$1 is not one crate" >&2
    exit 1
  fi
  echo "${2%/}"
}
published=$(unpacked "$1" published)
packaged=$(unpacked "$2" packaged)
name=${published##*/}
# The core is published only when its version moves, so an unmoved change never reaches crates.io.
# Cargo.toml holds what the root's [workspace.dependencies] resolve to, Cargo.toml.orig does not.
for file in src Cargo.toml.orig Cargo.toml; do
  if ! diff -r "$published/$file" "$packaged/$file" >&2; then
    echo "$name on crates.io is not what this tree packages: raise the version in" \
      "crates/pitboard-core/Cargo.toml" >&2
    exit 1
  fi
done
