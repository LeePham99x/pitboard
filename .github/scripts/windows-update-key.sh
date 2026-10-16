#!/bin/sh
# Compares the Windows update key a release's MSI carries with the one the newest earlier release
# with an MSI shipped, betas included. An installed copy takes an update only when the key it
# carries verifies it, and WinSparkle has no second route, so a changed key strands every copy
# unless a rotation means it.
set -eu

if [ "$#" -ne 5 ]; then
  echo "usage: windows-update-key.sh <version> <this release's key> <earlier release with an MSI," \
    "or ''> <its key> <WINDOWS_UPDATE_KEY_ROTATION>" >&2
  exit 2
fi
version=$1
now=$2
previous=$3
was=$4
rotation=$5

if [ -z "$now" ]; then
  echo "this release's MSI carries no update key: WINDOWS_UPDATE_PUBLIC_KEY is not set" >&2
  exit 1
fi
if [ -z "$previous" ]; then
  echo "no earlier release ships an MSI, so there is no key to compare with"
  exit 0
fi
# Every MSI a release published carries a key, so none read is a broken read, not a pass.
if [ -z "$was" ]; then
  echo "no update key was read from $previous's MSI" >&2
  exit 1
fi
if [ "$now" = "$was" ]; then
  echo "the update key is the one $previous shipped"
  exit 0
fi
if [ "$rotation" = "$version" ]; then
  echo "declared rotation: $previous shipped $was, $version ships $now"
  exit 0
fi
echo "this release's update key is $now, and $previous shipped $was." >&2
echo "A copy installed from $previous takes no update the old key does not verify. Read" >&2
echo "'Rotate the Windows update key' in RELEASING.md, and set the repository variable" >&2
echo "WINDOWS_UPDATE_KEY_ROTATION to $version when this release means to." >&2
exit 1
