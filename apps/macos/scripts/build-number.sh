#!/bin/sh
# Prints the app's CFBundleVersion for a Cargo.toml version, X.Y.Z or X.Y.Z-beta.N.
set -eu

if [ "$#" -ne 1 ]; then
    echo "usage: build-number.sh <version>" >&2
    exit 2
fi
version=$1

# Sparkle orders updates by this number: a beta sits below its release, above the one before.
two='(0|[1-9][0-9]?)'
beta='-beta\.([1-9]|[1-8][0-9]|9[0-8])'
ok=false
case $version in
    *[!0-9A-Za-z.-]* | "") ;;
    *)
        if printf '%s\n' "$version" | grep -qxE "(0|[1-9][0-9]{0,3})\.$two\.$two($beta)?"; then
            ok=true
        fi
        ;;
esac
if [ "$ok" != true ]; then
    echo "$version has no build number: it takes X.Y.Z or X.Y.Z-beta.N, with Y and Z at" \
        "most 99 and N from 1 to 98" >&2
    exit 1
fi

base=${version%%-*}
major=${base%%.*}
rest=${base#*.}
minor=${rest%%.*}
patch=${rest#*.}
case $version in
    *-beta.*) step=${version##*-beta.} ;;
    *) step=99 ;;
esac
echo $((major * 1000000 + minor * 10000 + patch * 100 + step))
