#!/usr/bin/env fish
# Installer for the nebra CLI on systems running fish (e.g. CachyOS).
#
# Detects the host OS + architecture, downloads the matching release archive
# from https://github.com/nebra-lang/nebra/releases/latest, extracts it to
# ~/.nebra, symlinks the binary into ~/.local/bin, and persists ~/.local/bin
# on $fish_user_paths so `nebra` works in any new fish session. Honour:
#   NEBRA_INSTALL_DIR  - where to extract the archive (default ~/.nebra)
#   NEBRA_BIN_DIR      - where the `nebra` symlink goes  (default ~/.local/bin)
#   NEBRA_VERSION      - install a specific tag       (default: latest)
#
# Usage: curl -fsSL https://raw.githubusercontent.com/nebra-lang/nebra/master/scripts/install.fish | fish

set repo "nebra-lang/nebra"
set install_dir (test -n "$NEBRA_INSTALL_DIR"; and echo $NEBRA_INSTALL_DIR; or echo "$HOME/.nebra")
set bin_dir     (test -n "$NEBRA_BIN_DIR";     and echo $NEBRA_BIN_DIR;     or echo "$HOME/.local/bin")
set tag         (test -n "$NEBRA_VERSION";     and echo $NEBRA_VERSION;     or echo "latest")

function _nebra_err
    set_color red; printf 'error: '; set_color normal
    echo $argv >&2
    exit 1
end

function _nebra_info
    set_color cyan; printf '==> '; set_color normal
    echo $argv
end

command -q curl; or _nebra_err "curl is required."
command -q tar;  or _nebra_err "tar is required."

switch (uname -s)
    case Linux
        set os_tag "linux"
    case Darwin
        set os_tag "osx"
    case '*'
        _nebra_err "Unsupported OS: "(uname -s)" (only Linux and macOS are supported by this script)."
end

switch (uname -m)
    case x86_64 amd64
        set arch_tag "x64"
    case aarch64 arm64
        set arch_tag "arm64"
    case '*'
        _nebra_err "Unsupported architecture: "(uname -m)
end

set archive "nebra-$os_tag-$arch_tag.tar.gz"

if test "$tag" = "latest"
    _nebra_info "Resolving latest release tag..."
    set tag (curl -fsSL "https://api.github.com/repos/$repo/releases/latest" \
        | string match -r '"tag_name":\s*"([^"]+)"' \
        | string match -rg '"tag_name":\s*"([^"]+)"' \
        | head -n1)
    test -n "$tag"; or _nebra_err "Could not determine latest release tag."
end

set url "https://github.com/$repo/releases/download/$tag/$archive"
_nebra_info "Downloading $archive ($tag)..."

set tmp (mktemp -d)
function _nebra_cleanup --on-event fish_exit
    rm -rf $tmp 2>/dev/null
end

curl -fSL --progress-bar -o "$tmp/$archive" "$url"
or _nebra_err "Download failed: $url"

# Best-effort checksum verification (release ships a .sha256 sibling).
if curl -fsSL -o "$tmp/$archive.sha256" "$url.sha256" 2>/dev/null
    _nebra_info "Verifying checksum..."
    pushd $tmp >/dev/null
    if command -q sha256sum
        sha256sum -c "$archive.sha256" >/dev/null; or _nebra_err "Checksum verification failed."
    else if command -q shasum
        shasum -a 256 -c "$archive.sha256" >/dev/null; or _nebra_err "Checksum verification failed."
    end
    popd >/dev/null
end

_nebra_info "Extracting to $install_dir..."
mkdir -p $install_dir
tar -xzf "$tmp/$archive" -C $install_dir
chmod +x "$install_dir/nebra"

_nebra_info "Linking $bin_dir/nebra -> $install_dir/nebra"
mkdir -p $bin_dir
ln -sf "$install_dir/nebra" "$bin_dir/nebra"
ln -sf "$install_dir/nebra" "$bin_dir/neb"

# Persist $bin_dir on fish_user_paths so it survives new shells without
# requiring a config.fish edit. Universal scope, so this is permanent.
if not contains $bin_dir $fish_user_paths
    set -U fish_user_paths $bin_dir $fish_user_paths
    _nebra_info "Added $bin_dir to fish_user_paths."
end

echo
_nebra_info "nebra $tag installed."
_nebra_info "Try: nebra version"
