#!/bin/sh
# The session bundle is the beta candidate as it ships: a branch at 1.0.0-beta.1 or later, no cfg, an age key.
set -eu

if [ "$#" -ne 3 ]; then
  echo "usage: session-gate.sh <ref> <version> <age public key>" >&2
  exit 2
fi
ref=$1
version=$2
key=$3
only="the session bundle is built only from a branch whose Pitboard version is 1.0.0-beta.1 or later"

case $ref in
  refs/heads/?*) ;;
  *)
    echo "$ref is not a branch: $only" >&2
    exit 1
    ;;
esac

numbers=$(sh "$(dirname "$0")/windows-versions.sh" "$version")
case $version in
  0.*)
    echo "Pitboard is $version on this branch: $only" >&2
    exit 1
    ;;
esac

refuse_cfg() {
  case $2 in
    *pitboard_unreleased_windows*)
      echo "$1 opens the build to Windows before its release: the session bundle is built as a" \
        "release is, with no cfg" >&2
      exit 1
      ;;
  esac
}
refuse_cfg RUSTFLAGS "${RUSTFLAGS-}"
refuse_cfg CARGO_ENCODED_RUSTFLAGS "${CARGO_ENCODED_RUSTFLAGS-}"

# A refusal never prints it: a secret key pasted in its place would reach the run's log.
not_a_key() {
  echo "the key given is not an age public key, which age-keygen prints as age1 and 58" \
    "characters of Bech32's alphabet: the session bundle leaves the runner only encrypted" >&2
  exit 1
}
# grep matches line by line, so a newline in the key would get past it.
case $key in
  *[!0-9a-z]* | "") not_a_key ;;
esac
printf '%s\n' "$key" | grep -qxE 'age1[qpzry9x8gf2tvdw0s3jn54khce6mua7l]{58}' || not_a_key

file=$(printf '%s\n' "$numbers" | sed -n 's/^file=//p')
echo "version=$version"
printf '%s\n' "$numbers"
echo "msi-b=${file%.*}.$((${file##*.} + 1))"
echo "recipient=$key"
