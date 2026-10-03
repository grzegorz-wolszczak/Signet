#!/usr/bin/env bash
# Deterministically generates minimal binary files for the epub3/media fixture.
# Run it from any directory; the files land relative to this script.
# These files are NOT real media — they serve for round-trip (byte-for-byte) tests and manifest parsing.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
media="$here/epub3/media/EPUB"

mkdir -p "$media/images" "$media/fonts" "$media/audio"

# 1x1 PNG (transparent) — a canonical, well-known byte sequence.
png1x1='iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
printf '%s' "$png1x1" | base64 -d > "$media/images/cover.png"

# 2x1 PNG — a file different from cover, for comparison tests and "figure".
png2x1='iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEklEQVR42mP8z8BQz0AEYBxVCAB35wX9pQ4I6wAAAABJRU5ErkJggg=='
printf '%s' "$png2x1" | base64 -d > "$media/images/figure.png"

# A TTF font dummy: the sfnt prefix 0x00010000 + 2048 B of deterministic filler
# (>= 1040 B, so that IDPF obfuscation can be tested).
{
  printf '\x00\x01\x00\x00'
  perl -e 'print chr($_ % 251) for 0..2047'
} > "$media/fonts/font.ttf"

# An MP3 dummy: the sync frame 0xFFFB + 1024 B of deterministic filler.
{
  printf '\xff\xfb\x90\x00'
  perl -e 'print chr((7 * $_ + 3) % 256) for 0..1023'
} > "$media/audio/clip.mp3"

echo "Generated:"
for f in images/cover.png images/figure.png fonts/font.ttf audio/clip.mp3; do
  printf '  %-22s %6d B\n' "$f" "$(wc -c < "$media/$f")"
done
