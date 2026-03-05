#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
INSTALL_SCRIPT="$ROOT_DIR/install.sh"

assert_contains() {
  local needle="$1"
  local haystack_file="$2"
  if ! grep -F "$needle" "$haystack_file" >/dev/null 2>&1; then
    echo "Expected to find: $needle"
    echo "--- file contents ---"
    cat "$haystack_file"
    echo "---------------------"
    exit 1
  fi
}

assert_not_contains() {
  local needle="$1"
  local haystack_file="$2"
  if grep -F "$needle" "$haystack_file" >/dev/null 2>&1; then
    echo "Did not expect to find: $needle"
    echo "--- file contents ---"
    cat "$haystack_file"
    echo "---------------------"
    exit 1
  fi
}

make_fake_archive() {
  local archive_path="$1"
  local workdir
  workdir="$(mktemp -d)"
  trap "rm -rf '$workdir'" RETURN
  cat >"$workdir/git-memento" <<'EOF'
#!/bin/sh
echo "git-memento test binary"
EOF
  chmod +x "$workdir/git-memento"
  tar -czf "$archive_path" -C "$workdir" git-memento
}

run_case() {
  local include_install_dir="$1"
  local tmpdir fakebin log_file archive_path install_dir path_value

  tmpdir="$(mktemp -d)"
  fakebin="$tmpdir/fakebin"
  log_file="$tmpdir/install.log"
  archive_path="$tmpdir/git-memento-linux-x64.tar.gz"
  install_dir="$tmpdir/install/bin"
  mkdir -p "$fakebin"

  make_fake_archive "$archive_path"

  cat >"$fakebin/uname" <<'EOF'
#!/bin/sh
if [ "${1:-}" = "-s" ]; then
  echo "Linux"
elif [ "${1:-}" = "-m" ]; then
  echo "x86_64"
else
  echo "unsupported uname invocation" >&2
  exit 1
fi
EOF
  chmod +x "$fakebin/uname"

  cat >"$fakebin/curl" <<EOF
#!/bin/sh
set -eu
if [ "\$#" -eq 4 ] && [ "\$1" = "-fsSL" ] && [ "\$3" = "-o" ]; then
  cp "$archive_path" "\$4"
  exit 0
fi
echo "unexpected curl invocation: \$*" >&2
exit 1
EOF
  chmod +x "$fakebin/curl"

  path_value="$fakebin:/usr/bin:/bin"
  if [ "$include_install_dir" = "true" ]; then
    path_value="$install_dir:$path_value"
  fi

  (
    export PATH="$path_value"
    export GIT_MEMENTO_INSTALL_DIR="$install_dir"
    sh "$INSTALL_SCRIPT" >"$log_file" 2>&1
  )

  if [ ! -x "$install_dir/git-memento" ]; then
    echo "Expected installed binary at $install_dir/git-memento"
    exit 1
  fi

  if [ "$include_install_dir" = "true" ]; then
    assert_contains "$install_dir is already in your PATH." "$log_file"
    assert_not_contains "$install_dir is not currently in your PATH." "$log_file"
  else
    assert_contains "$install_dir is not currently in your PATH." "$log_file"
    assert_contains "export PATH=\"$install_dir:\$PATH\"" "$log_file"
  fi

  rm -rf "$tmpdir"
}

run_case "false"
run_case "true"
echo "install.sh PATH tests passed"
