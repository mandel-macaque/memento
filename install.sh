#!/bin/sh
set -eu

REPO="mandel-macaque/memento"
INSTALL_DIR="${GIT_MEMENTO_INSTALL_DIR:-$HOME/.local/bin}"

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
    case "$uname_m" in
      x86_64|amd64|AMD64) asset="git-memento-win-x64.zip" ;;
      *) echo "Unsupported Windows architecture: $uname_m (only x64 release asset is published)." >&2; exit 1 ;;
    esac
    ;;
  *)
    echo "Unsupported OS: $uname_s" >&2
    exit 1
    ;;
esac

download_url="https://github.com/$REPO/releases/latest/download/$asset"
major_tag_url="https://github.com/$REPO/releases/download/v1/$asset"
releases_api_url="https://api.github.com/repos/$REPO/releases?per_page=100"

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

archive="$tmpdir/$asset"
downloader=""
if command -v curl >/dev/null 2>&1; then
  downloader="curl"
elif command -v wget >/dev/null 2>&1; then
  downloader="wget"
else
  echo "Neither curl nor wget is available to download release assets." >&2
  exit 1
fi

try_download() {
  url="$1"
  tmp_archive="$archive.part"
  rm -f "$tmp_archive"
  case "$downloader" in
    curl)
      if curl -fsSL "$url" -o "$tmp_archive" 2>/dev/null; then
        mv "$tmp_archive" "$archive"
        return 0
      fi
      ;;
    wget)
      if wget -qO "$tmp_archive" "$url" 2>/dev/null; then
        mv "$tmp_archive" "$archive"
        return 0
      fi
      ;;
  esac
  rm -f "$tmp_archive"
  return 1
}

read_urls() {
  url="$1"
  case "$downloader" in
    curl)
      curl -fsSL "$url"
      ;;
    wget)
      wget -qO- "$url"
      ;;
  esac
}

if ! try_download "$download_url"; then
  echo "Latest release is missing $asset; trying fallback release tags..." >&2
  if ! try_download "$major_tag_url"; then
    echo "Fallback tag v1 is missing $asset; scanning published release assets..." >&2
    release_urls="$(read_urls "$releases_api_url" | sed -n "s/.*\"browser_download_url\": \"\\([^\"]*\\/$asset\\)\".*/\\1/p" || true)"
    downloaded="false"
    if [ -n "$release_urls" ]; then
      old_ifs="$IFS"
      IFS='
'
      for candidate in $release_urls; do
        if [ -n "$candidate" ]; then
          if try_download "$candidate"; then
            downloaded="true"
            break
          fi
        fi
      done
      IFS="$old_ifs"
    fi

    if [ "$downloaded" != "true" ]; then
      echo "Could not download $asset from latest release, v1, or release assets in $REPO" >&2
      exit 1
    fi
  fi
fi

mkdir -p "$INSTALL_DIR"

case "$asset" in
  *.tar.gz)
    tar -xzf "$archive" -C "$tmpdir"
    cp "$tmpdir/git-memento" "$INSTALL_DIR/git-memento"
    chmod +x "$INSTALL_DIR/git-memento"
    ;;
  *.zip)
    if command -v unzip >/dev/null 2>&1; then
      unzip -q "$archive" -d "$tmpdir"
    elif command -v powershell.exe >/dev/null 2>&1; then
      powershell.exe -NoProfile -Command "Expand-Archive -Path '$archive' -DestinationPath '$tmpdir' -Force" >/dev/null
    else
      echo "zip extraction requires unzip or powershell.exe." >&2
      exit 1
    fi
    cp "$tmpdir/git-memento.exe" "$INSTALL_DIR/git-memento.exe"
    ;;
esac

echo "Installed git-memento to $INSTALL_DIR"

normalize_dir() {
  dir="$1"
  while [ "${dir%/}" != "$dir" ]; do
    dir="${dir%/}"
  done
  printf "%s" "$dir"
}

path_has_dir_with_delim() {
  delim="$1"
  target="$2"
  old_ifs="$IFS"
  IFS="$delim"
  for path_entry in $PATH; do
    if [ "$(normalize_dir "$path_entry")" = "$target" ]; then
      IFS="$old_ifs"
      return 0
    fi
  done
  IFS="$old_ifs"
  return 1
}

install_dir_normalized="$(normalize_dir "$INSTALL_DIR")"
path_contains_install_dir="false"
if path_has_dir_with_delim ":" "$install_dir_normalized" || path_has_dir_with_delim ";" "$install_dir_normalized"; then
  path_contains_install_dir="true"
fi

if [ "$path_contains_install_dir" = "true" ]; then
  echo "$INSTALL_DIR is already in your PATH."
else
  echo "$INSTALL_DIR is not currently in your PATH."
  echo "Add it for your current shell session:"
  echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
  echo "Persist this in your shell profile (for example ~/.zshrc or ~/.bashrc), then restart your shell."
fi
