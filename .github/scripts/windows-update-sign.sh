#!/bin/sh
# Signs both MSIs of a Windows release with the private key on standard input and checks each signature with the
# public key the MSIs carry, so a pair that disagrees fails a beta too. Only the latest full release gets <feed>.
set -eu

if [ "$#" -lt 8 ]; then
  echo "usage: windows-update-sign.sh <tag> <latest> <public key> <folder> <downloads> <feed> <sign_update> <verifier>..." >&2
  exit 2
fi
tag=$1
latest=$2
public=$3
folder=$4
downloads=$5
feed=$6
sign=$7
shift 7

private=$(cat)
if [ -z "$private" ]; then
  echo "WINDOWS_UPDATE_PRIVATE_KEY is not set in the windows-update environment: the MSIs cannot be signed" >&2
  exit 1
fi

signatures=
for arch in x64 arm64; do
  msi=$folder/Pitboard-$tag-windows-$arch.msi
  signature=$(printf '%s\n' "$private" | "$sign" --ed-key-file - -p "$msi")
  if ! "$@" "$public" "$signature" "$msi" >&2; then
    echo "WINDOWS_UPDATE_PRIVATE_KEY is not WINDOWS_UPDATE_PUBLIC_KEY's pair: no Release build would take $msi" >&2
    exit 1
  fi
  signatures="$signatures $signature"
done

if [ "$latest" != true ]; then
  echo "Both MSIs verify with the Windows update public key. Only the latest full release has a feed."
  exit 0
fi
# Each signature is base64, with no space in it.
# shellcheck disable=SC2086
python3 "$(dirname "$0")/windows-appcast.py" "${tag#v}" "$downloads" "$folder" $signatures > "$feed"
if [ "$(grep -c 'sparkle:edSignature="' "$feed")" != 2 ]; then
  echo "appcast-windows.xml does not carry a signature for each MSI" >&2
  exit 1
fi
echo "Both MSIs verify with the Windows update public key, and $feed lists them."
