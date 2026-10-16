#!/bin/sh
# Prints the numbers a Windows build carries for a Cargo.toml version, X.Y.Z or X.Y.Z-beta.N, as
# key=value lines: the MSI's ProductVersion, the version resource's FileVersion, which
# crates/pitboard/build/versioninfo.rs writes the same way, and PitboardShare's package version.
set -eu

if [ "$#" -ne 1 ]; then
  echo "usage: windows-versions.sh <version>" >&2
  exit 2
fi
version=$1

refuse() {
  echo "$version has no Windows version numbers: it takes X.Y.Z or X.Y.Z-beta.N, with X and Y" \
    "at most 255, Z at most 65535 and N from 1 to 999" >&2
  exit 1
}

# grep matches line by line, so a newline in the version would get past it.
case $version in
  *[!0-9A-Za-z.-]* | "") refuse ;;
esac
number='(0|[1-9][0-9]{0,4})'
printf '%s\n' "$version" | grep -qxE "$number\.$number\.$number(-beta\.[1-9][0-9]{0,2})?" ||
  refuse

base=${version%%-*}
major=${base%%.*}
rest=${base#*.}
minor=${rest%%.*}
patch=${rest#*.}
# The MSI's limits; the other two take 65535 in every field.
if [ "$major" -gt 255 ] || [ "$minor" -gt 255 ] || [ "$patch" -gt 65535 ]; then
  refuse
fi

# Beta N is X.Y.Z.N and a final X.Y.Z.1000, so each version numbers above the one before it.
# Windows Installer compares three fields and ignores the fourth.
case $version in
  *-beta.*)
    fourth=${version##*-beta.}
    msi=$base.$fourth
    ;;
  *)
    fourth=1000
    msi=$base
    ;;
esac
echo "msi=$msi"
echo "file=$base.$fourth"
echo "package=$base.$fourth"
