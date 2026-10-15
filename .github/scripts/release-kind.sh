#!/bin/sh
# What a release tag makes, for release.yml's guard: key=value lines for $GITHUB_OUTPUT.
set -eu

if [ "$#" -ne 3 ]; then
  echo "usage: release-kind.sh <tag> <manifest version> <published full tags file>" >&2
  exit 2
fi
tag=$1
manifest=$2
published=$3

refuse() {
  echo "$1" >&2
  exit 1
}

# SemVer's numbers: no leading zero.
number='(0|[1-9][0-9]*)'
release="$number\.$number\.$number"

matches() {
  printf '%s\n' "$1" | grep -qxE "$2"
}

above() {
  awk -v a="$1" -v b="$2" 'BEGIN {
    split(a, x, "."); split(b, y, ".")
    for (i = 1; i <= 3; i++) {
      if (x[i] + 0 > y[i] + 0) exit 0
      if (x[i] + 0 < y[i] + 0) exit 1
    }
    exit 1
  }'
}

# grep matches line by line, so a newline in a tag would get past it.
case $tag in
  v*[!0-9A-Za-z.-]* | v | [!v]* | "")
    refuse "$tag is not a release tag: vX.Y.Z, vX.Y.Z-rcN or vX.Y.Z-beta.N"
    ;;
esac
version=${tag#v}
if matches "$version" "$release"; then
  kind=full
  base=$version
elif matches "$version" "$release-rc[1-9][0-9]*"; then
  kind=rc
  base=${version%-rc*}
elif matches "$version" "$release-beta\.([1-9]|[1-8][0-9]|9[0-8])"; then
  kind=beta
  base=$version
else
  refuse "$tag is not a release tag: vX.Y.Z, vX.Y.Z-rcN or vX.Y.Z-beta.N"
fi
if [ "$base" != "$manifest" ]; then
  refuse "tag $tag does not match Cargo.toml's $manifest"
fi
major=${base%%.*}
if [ "$kind" = beta ] && [ "$major" = 0 ]; then
  refuse "$tag is a beta of a 0.x release: betas are Windows betas, which start at 1.0.0"
fi

prerelease=true
crates=false
latest=false
case $kind in
  full) prerelease=false crates=true latest=true ;;
  beta) crates=true ;;
esac
# Never latest below a published full release, so install.sh, the feeds and the tap never go back.
if [ "$latest" = true ]; then
  while IFS= read -r line || [ -n "$line" ]; do
    [ -n "$line" ] || continue
    other=${line#v}
    if [ "v$other" != "$line" ] || ! matches "$other" "$release"; then
      refuse "the published full release $line has no vX.Y.Z tag"
    fi
    if above "$other" "$base"; then
      latest=false
    fi
  done <"$published"
fi
windows=true
if [ "$major" = 0 ]; then
  windows=false
fi

echo "version=$version"
echo "base=$base"
echo "kind=$kind"
echo "prerelease=$prerelease"
echo "windows=$windows"
echo "crates=$crates"
echo "latest=$latest"
