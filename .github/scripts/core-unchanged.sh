#!/bin/sh
# Refuses a pitboard-core on crates.io whose source or manifest differs from the tree's.
set -eu

if [ "$#" -ne 2 ]; then
  echo "usage: core-unchanged.sh <published .crate> <core directory>" >&2
  exit 2
fi
crate=$1
core=$2
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

tar -xzf "$crate" -C "$work"
set -- "$work"/*/
if [ "$#" -ne 1 ] || [ ! -f "$1/Cargo.toml.orig" ]; then
  echo "$crate is not one crate" >&2
  exit 1
fi
published=${1%/}
name=${published##*/}
# The core is published only when its version moves, so an unmoved change never reaches crates.io.
if ! diff -r "$published/src" "$core/src" >&2 ||
  ! diff "$published/Cargo.toml.orig" "$core/Cargo.toml" >&2; then
  echo "$name on crates.io is not what $core holds: raise the version in $core/Cargo.toml" >&2
  exit 1
fi
