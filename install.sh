#!/bin/sh
set -eu

REPO="${GIT_MEMENTO_REPO:-<YOUR_ORG>/<YOUR_REPO>}"
INSTALL_DIR="${GIT_MEMENTO_INSTALL_DIR:-$HOME/.local/bin}"

if [ "$REPO" = "<YOUR_ORG>/<YOUR_REPO>" ]; then
  echo "Set GIT_MEMENTO_REPO to your GitHub repo (owner/name)." >&2
  exit 1
fi

uname_s="$(uname -s)"
uname_m="$(uname -m)"

case "$uname_s" in
  Darwin)
    case "$uname_m" in
      arm64) asset="git-memento-osx-arm64.tar.gz" ;;
      x86_64) asset="git-memento-osx-x64.tar.gz" ;;
      *) echo "Unsupported macOS architecture: $uname_m" >&2; exit 1 ;;
    esac
    ;;
  Linux)
    case "$uname_m" in
      x86_64) asset="git-memento-linux-x64.tar.gz" ;;
      *) echo "Unsupported Linux architecture: $uname_m (only x86_64 release asset is published)." >&2; exit 1 ;;
    esac
    ;;
  MINGW*|MSYS*|CYGWIN*|Windows_NT)
    asset="git-memento-win-x64.zip"
    ;;
  *)
    echo "Unsupported OS: $uname_s" >&2
    exit 1
    ;;
esac

api_url="https://api.github.com/repos/$REPO/releases/latest"
download_url="$(curl -fsSL "$api_url" | sed -n "s/.*\"browser_download_url\": \"\\([^\"]*${asset}\\)\".*/\\1/p" | head -n1)"

if [ -z "$download_url" ]; then
  echo "Could not find asset $asset in latest release of $REPO" >&2
  exit 1
fi

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

archive="$tmpdir/$asset"
curl -fsSL "$download_url" -o "$archive"

mkdir -p "$INSTALL_DIR"

case "$asset" in
  *.tar.gz)
    tar -xzf "$archive" -C "$tmpdir"
    cp "$tmpdir/git-memento" "$INSTALL_DIR/git-memento"
    chmod +x "$INSTALL_DIR/git-memento"
    ;;
  *.zip)
    if ! command -v unzip >/dev/null 2>&1; then
      echo "unzip is required on Windows-like shells." >&2
      exit 1
    fi
    unzip -q "$archive" -d "$tmpdir"
    cp "$tmpdir/git-memento.exe" "$INSTALL_DIR/git-memento.exe"
    ;;
esac

echo "Installed git-memento to $INSTALL_DIR"
echo "Ensure $INSTALL_DIR is in your PATH."
