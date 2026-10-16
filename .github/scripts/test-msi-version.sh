#!/bin/sh
# Tests the version the MSI carries, Pitboard.Installer.wixproj's PitboardMsiVersion, through
# MSBuild alone: nothing is built, so it runs wherever the .NET SDK does.
set -eu

# From apps/windows, whose global.json names the SDK and WiX's.
cd "$(dirname "$0")/../../apps/windows"
project=Pitboard.Installer
failed=0

numbers() {
  got=$(dotnet msbuild "$project" -nologo -getProperty:PitboardMsiVersion -p:PitboardVersion="$1")
  if [ "$got" != "$2" ]; then
    echo "$1 gave the MSI $got, not $2"
    failed=1
  elif ! dotnet msbuild "$project" -nologo -v:q -t:CheckPitboardMsiVersion -p:PitboardVersion="$1" >/dev/null; then
    echo "$1 was refused"
    failed=1
  else
    echo "ok: $1 is $2"
  fi
}

refuses() {
  if dotnet msbuild "$project" -nologo -v:q -t:CheckPitboardMsiVersion -p:PitboardVersion="$1" >/dev/null 2>&1; then
    echo "$1 was taken"
    failed=1
  else
    echo "ok: $1 is refused"
  fi
}

# Part 3's table of version numbers, its MSI ProductVersion row.
numbers 0.15.0 0.15.0
numbers 1.0.0-beta.1 1.0.0.1
numbers 1.0.0 1.0.0
numbers 1.0.1 1.0.1
numbers 1.1.0-beta.2 1.1.0.2
numbers 255.255.65535 255.255.65535
refuses ''
refuses 1.0.0-rc.1
refuses 1.0.0-beta
refuses 1.0
refuses 256.0.0
refuses 1.256.0
refuses 1.0.65536
refuses 1.0.0-beta.65536
exit "$failed"
