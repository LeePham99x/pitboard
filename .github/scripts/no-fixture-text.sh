#!/bin/sh
# Fails when a library built without the `fixture` feature holds a fixture world's text: a word
# of its stand-in pages, an account of its worlds, or the variable only a build for tests reads.
# build-app.sh looks for the same text in the macOS app.
set -eu

if [ "$#" -eq 0 ]; then
  echo "usage: no-fixture-text.sh <library>..." >&2
  exit 2
fi
for library in "$@"; do
  if [ ! -s "$library" ]; then
    echo "there is no $library to look in" >&2
    exit 1
  fi
  if LC_ALL=C grep -a -q -e 'A Pitboard fixture page' -e 'dana@work.example' \
    -e 'PITBOARD_TEST_CLAUDE_DESKTOP' "$library"; then
    echo "$library was built without the fixture feature and holds a fixture" >&2
    exit 1
  fi
done
