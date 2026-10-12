#!/bin/sh
# Writes a packaging/ template with a release's version and checksums: the casks and install.sh.
set -eu

if [ "$#" -ne 4 ]; then
  echo "usage: fill-template.sh <template> <out> <version> <SHA256SUMS>" >&2
  exit 2
fi
template=$1
out=$2
version=$3
sums=$4
work=$out.filling
trap 'rm -f "$work" "$work.sed"' EXIT

# install.sh carries it as shell, and a tag can hold $( or a backtick.
case $version in
  *[!0-9A-Za-z.+-]* | "")
    echo "\"$version\" is not a version" >&2
    exit 1
    ;;
esac

# A file the release has no line for is one nobody can install, so it stops the release here.
checksum() {
  line=$(awk -v f="$1" '$2 == f { n++; sum = $1 } END { if (n == 1) print sum }' "$sums")
  if ! printf '%s\n' "$line" | grep -qxE '[0-9a-f]{64}'; then
    echo "SHA256SUMS does not have exactly one line for $1:" >&2
    cat "$sums" >&2
    return 1
  fi
  printf '%s' "$line"
}

value() {
  case $1 in
    @VERSION@) printf '%s' "$version" ;;
    @SHA256_AARCH64_APPLE_DARWIN@) checksum "pitboard-v$version-aarch64-apple-darwin.tar.gz" ;;
    @SHA256_X86_64_APPLE_DARWIN@) checksum "pitboard-v$version-x86_64-apple-darwin.tar.gz" ;;
    @SHA256_AARCH64_UNKNOWN_LINUX_MUSL@)
      checksum "pitboard-v$version-aarch64-unknown-linux-musl.tar.gz"
      ;;
    @SHA256_X86_64_UNKNOWN_LINUX_MUSL@)
      checksum "pitboard-v$version-x86_64-unknown-linux-musl.tar.gz"
      ;;
    @SHA256_APP@) checksum "Pitboard-v$version-macos.zip" ;;
  esac
}

if ! grep -qF @VERSION@ "$template"; then
  echo "$template has no @VERSION@" >&2
  exit 1
fi
cp "$template" "$work"
# A replacement that matched nothing would leave a placeholder, or the previous value, in place.
for name in @VERSION@ @SHA256_AARCH64_APPLE_DARWIN@ @SHA256_X86_64_APPLE_DARWIN@ \
  @SHA256_AARCH64_UNKNOWN_LINUX_MUSL@ @SHA256_X86_64_UNKNOWN_LINUX_MUSL@ @SHA256_APP@; do
  if ! grep -qF "$name" "$work"; then
    continue
  fi
  filled=$(value "$name")
  sed "s|$name|$filled|g" "$work" >"$work.sed"
  mv "$work.sed" "$work"
  if grep -qF "$name" "$work" || ! grep -qF "\"$filled\"" "$work"; then
    echo "$name was not replaced in $template" >&2
    exit 1
  fi
done
if grep -nE '@[A-Z0-9_]+@' "$work" >&2; then
  echo "$template has a placeholder this job does not fill" >&2
  exit 1
fi
mv "$work" "$out"
